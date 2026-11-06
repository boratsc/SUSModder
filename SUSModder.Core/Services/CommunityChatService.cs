using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SUSModder.Core.Api;
using SUSModder.Core.Diagnostics;
using SUSModder.Core.Models;
using SUSModder.Core.Utilities;
using SUSModder.Core.Validators;

namespace SUSModder.Core.Services
{
    /// <summary>
    /// Adapter globalnego komunikatora na istniejący klient API v2 (<see cref="ISUSModderApiClient"/>).
    ///
    /// Transport:
    /// - odczyt:  GET  chat?type=message&amp;modId=1&amp;limit=N
    /// - publikacja: POST chat  { "type": "message", "modId": 1, "content": "..." }
    /// - usuwanie: DELETE chat/{id}
    /// - zgłaszanie: POST chat/{id}/report
    ///
    /// Zasady:
    /// - stały <see cref="CommunityChatConstants.ChannelModId"/> niezależny od wybranego moda;
    /// - rozróżnienie: sukces / pusta rozmowa / awaria sieci / auth / limit / błąd protokołu;
    /// - sukces publikacji wymaga kompletnego zapisanego wpisu z serwerowym autorem PL/EN;
    ///   niepewna odpowiedź → <see cref="CommunityChatStatus.ProtocolError"/> i **bez automatycznego ponowienia**
    ///   (ryzyko duplikatu);
    /// - sygnał porażki w odpowiedzi (success=false, obiekt "error") jest odrzucany w obu wariantach:
    ///   w korzeniu odpowiedzi i w zagnieżdżonym obiekcie "data";
    /// - błędy pochodzące z odpowiedzi serwera są traktowane jako dane nieufne: do logów trafiają wyłącznie
    ///   kody z białej listy, nigdy surowe pola odpowiedzi;
    /// - logowanie wyłącznie operacji, statusu i kodu błędu — bez treści wiadomości i bez body odpowiedzi.
    /// </summary>
    public sealed class CommunityChatService : ICommunityChatService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly string[] EntryArrayNames = { "entries", "messages", "items", "results" };

        /// <summary>Klucze rozpoznawanej umowy odpowiedzi API (odczyt, publikacja, moderacja).</summary>
        private static readonly string[] KnownEnvelopeKeys =
        {
            "success", "id", "data", "error", "errorCode", "entries", "messages", "items", "results",
            "expiresAt", "moderationWarning", "entryId", "messageId", "deleted", "reported", "removed"
        };

        /// <summary>
        /// Biała lista kodów, które mogą trafić do logu diagnostycznego.
        /// Każdy kod spoza listy jest zapisywany jako "UNKNOWN" — do logów nie trafiają
        /// żadne dowolne pola zwrócone przez serwer.
        /// </summary>
        private static readonly HashSet<string> KnownSafeErrorCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            // Kody klienckie
            CommunityChatErrorCodes.ContentTooShort,
            CommunityChatErrorCodes.ContentTooLong,
            CommunityChatErrorCodes.ContentContainsNewline,
            CommunityChatErrorCodes.ContentContainsControlCharacter,
            CommunityChatErrorCodes.DisallowedUrl,
            CommunityChatErrorCodes.TooManyLinks,
            CommunityChatErrorCodes.Unauthorized,
            CommunityChatErrorCodes.Forbidden,
            CommunityChatErrorCodes.RateLimited,
            CommunityChatErrorCodes.ServerError,
            CommunityChatErrorCodes.NetworkError,
            CommunityChatErrorCodes.ProtocolError,
            CommunityChatErrorCodes.Cancelled,
            CommunityChatErrorCodes.NotFound,
            CommunityChatErrorCodes.RequestFailed,
            CommunityChatErrorCodes.UnknownError,

            // Kody backendu (lobby) uznane za stabilny kontrakt
            "RATE_LIMITED",
            "DAILY_LIMIT_REACHED",
            "DUPLICATE_MESSAGE",
            "CONTENT_BLOCKED",
            "USER_BANNED",
            "INTERNAL_ERROR",
            "UNAUTHORIZED",
            "FORBIDDEN",
            "NOT_FOUND",
            "NOT_OWNER",
            "ALREADY_REPORTED",
            "MOD_NOT_FOUND",
            "VALIDATION_ERROR",
            "CHAT_TYPE_NOT_SUPPORTED",
            "CHAT_UNAVAILABLE",
            "CHAT_AUTHOR_UNAVAILABLE",
            "UNKNOWN_ERROR",
            "ENTRY_NOT_FOUND",
            "MESSAGE_NOT_FOUND",
            "ENTRY_EXPIRED",
            "INVALID_MOD_ID",
            "INVALID_LOBBY_CODE",
            "SPAM_DETECTED"
        };

        private readonly IDiagnosticsOutput _log;
        private readonly ISUSModderApiClient _apiClient;
        private readonly string _userHash;

        /// <summary>Serializuje odczyty — w MVP dopuszczalny jest jeden aktywny odczyt naraz.</summary>
        private readonly SemaphoreSlim _readGate = new(1, 1);

        /// <summary>ID wiadomości opublikowanych w bieżącej sesji (fallback dla własności, gdy serwer nie zwraca isOwn).</summary>

        public CommunityChatService(
            IConfiguration configuration,
            IDiagnosticsOutput log,
            IHardwareIdProvider hardwareIdProvider,
            ISUSModderApiClient? apiClient = null)
        {
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            var rawHash = (hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider)))
                .GetAnonymousUserHash();
            _userHash = AnonymousUserHash.EnsureValid(rawHash);
            if (!string.Equals(rawHash, _userHash, StringComparison.Ordinal))
                _log.Write($"[CommunityChat] userHash znormalizowany (len {rawHash?.Length ?? 0} → {_userHash.Length})");

            _apiClient = apiClient ?? SUSModderApiClientProvider.TryGetDefault()
                ?? new SUSModderApiClient(configuration, log);
        }

        /// <inheritdoc />
        public int ChannelModId => CommunityChatConstants.ChannelModId;

        // ═══════════════════════════════════════════════════════════════
        // Odczyt historii
        // ═══════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<CommunityChatMessagesResult> GetMessagesAsync(
            int limit = CommunityChatLimits.MaxHistorySize,
            CancellationToken ct = default)
        {
            var clamped = Math.Clamp(limit, 1, CommunityChatLimits.MaxHistorySize);

            var query = new Dictionary<string, string?>
            {
                ["type"] = CommunityChatConstants.EntryType,
                ["modId"] = CommunityChatConstants.ChannelModId.ToString(CultureInfo.InvariantCulture),
                ["limit"] = clamped.ToString(CultureInfo.InvariantCulture)
            };

            var gateAcquired = false;
            try
            {
                await _readGate.WaitAsync(ct).ConfigureAwait(false);
                gateAcquired = true;

                using var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Get,
                    RelativePath = CommunityChatConstants.ResourcePath,
                    Query = query,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var envelope = ParseEnvelope(body);
                    var status = MapStatus(response.StatusCode);
                    _log.Write($"[CommunityChat] GET failed: HTTP {(int)response.StatusCode} status={status} code={SafeLogToken(envelope.ErrorCode)}");

                    return new CommunityChatMessagesResult
                    {
                        Status = status,
                        ErrorCode = ResolveErrorCode(envelope.ErrorCode, status),
                        ErrorMessage = envelope.ErrorMessage ?? DefaultMessageFor(status),
                        RetryAfterSeconds = ParseRetryAfter(response),
                        HttpStatusCode = (int)response.StatusCode
                    };
                }

                var envelopeOk = ParseEnvelope(body);
                if (!envelopeOk.IsValidJson)
                {
                    _log.Write("[CommunityChat] GET invalid response: malformed JSON");
                    return ProtocolErrorMessages((int)response.StatusCode);
                }

                // Sygnał porażki w odpowiedzi unieważnia wpisy, także gdy są poprawne.
                if (envelopeOk.HasFailure)
                {
                    _log.Write($"[CommunityChat] GET rejected by server: code={SafeLogToken(envelopeOk.ErrorCode)}");
                    return new CommunityChatMessagesResult
                    {
                        Status = CommunityChatStatus.RequestFailed,
                        ErrorCode = ResolveErrorCode(envelopeOk.ErrorCode, CommunityChatStatus.RequestFailed),
                        ErrorMessage = envelopeOk.ErrorMessage ?? DefaultMessageFor(CommunityChatStatus.RequestFailed),
                        HttpStatusCode = (int)response.StatusCode
                    };
                }

                var parsed = ParseEntries(envelopeOk, out var parseCode, out var parseMessage);
                if (parsed is null)
                {
                    _log.Write($"[CommunityChat] GET invalid response: code={SafeLogToken(parseCode)}");
                    return new CommunityChatMessagesResult
                    {
                        Status = CommunityChatStatus.ProtocolError,
                        ErrorCode = parseCode ?? CommunityChatErrorCodes.ProtocolError,
                        ErrorMessage = parseMessage ?? DefaultMessageFor(CommunityChatStatus.ProtocolError),
                        HttpStatusCode = (int)response.StatusCode
                    };
                }

                var messages = NormalizeMessages(parsed);
                messages = FilterExpiredMessages(messages, DateTimeOffset.UtcNow);
                messages = ClampMessages(messages, clamped);

                _log.Write($"[CommunityChat] GET ok: {messages.Count} wiadomości");

                return new CommunityChatMessagesResult
                {
                    Status = messages.Count == 0 ? CommunityChatStatus.Empty : CommunityChatStatus.Success,
                    Messages = messages,
                    HttpStatusCode = (int)response.StatusCode
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Write("[CommunityChat] GET anulowane");
                return CancelledMessages();
            }
            catch (Exception ex)
            {
                _log.Write($"[CommunityChat] GET exception: {ex.GetType().Name}");
                return new CommunityChatMessagesResult
                {
                    Status = CommunityChatStatus.NetworkError,
                    ErrorCode = CommunityChatErrorCodes.NetworkError,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.NetworkError)
                };
            }
            finally
            {
                if (gateAcquired)
                    _readGate.Release();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Publikacja
        // ═══════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<CommunityChatSendResult> SendMessageAsync(string content, CancellationToken ct = default)
        {
            var (isValid, errorCode) = CommunityChatMessageValidator.Validate(content);
            if (!isValid)
            {
                _log.Write($"[CommunityChat] POST odrzucone lokalnie: code={SafeLogToken(errorCode)}");
                return new CommunityChatSendResult
                {
                    Status = CommunityChatStatus.ValidationFailed,
                    ErrorCode = errorCode ?? CommunityChatErrorCodes.ContentTooShort,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.ValidationFailed)
                };
            }

            var normalized = CommunityChatMessageValidator.Normalize(content) ?? string.Empty;

            var payload = new Dictionary<string, object?>
            {
                ["type"] = CommunityChatConstants.EntryType,
                ["modId"] = CommunityChatConstants.ChannelModId,
                ["content"] = normalized
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = CommunityChatConstants.ResourcePath,
                    Content = httpContent,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var envelope = ParseEnvelope(body);
                    var status = MapStatus(response.StatusCode);
                    _log.Write($"[CommunityChat] POST failed: HTTP {(int)response.StatusCode} status={status} code={SafeLogToken(envelope.ErrorCode)}");

                    return new CommunityChatSendResult
                    {
                        Status = status,
                        ErrorCode = ResolveErrorCode(envelope.ErrorCode, status),
                        ErrorMessage = envelope.ErrorMessage ?? DefaultMessageFor(status),
                        RetryAfterSeconds = ParseRetryAfter(response),
                        HttpStatusCode = (int)response.StatusCode
                    };
                }

                var envelopeOk = ParseEnvelope(body);

                // 1) Odpowiedź musi być poprawnym JSON-em.
                if (!envelopeOk.IsValidJson)
                {
                    _log.Write("[CommunityChat] POST uncertain response: malformed JSON (no retry)");
                    return ProtocolErrorSend((int)response.StatusCode);
                }

                // 2) Sygnał porażki w odpowiedzi (root lub data) — nawet przy 2xx.
                if (envelopeOk.HasFailure)
                {
                    _log.Write($"[CommunityChat] POST rejected by server: code={SafeLogToken(envelopeOk.ErrorCode)}");
                    return new CommunityChatSendResult
                    {
                        Status = CommunityChatStatus.RequestFailed,
                        ErrorCode = ResolveErrorCode(envelopeOk.ErrorCode, CommunityChatStatus.RequestFailed),
                        ErrorMessage = envelopeOk.ErrorMessage ?? DefaultMessageFor(CommunityChatStatus.RequestFailed),
                        HttpStatusCode = (int)response.StatusCode
                    };
                }

                // 3) Rozpoznawalna umowa odpowiedzi.
                if (!envelopeOk.IsRecognized)
                {
                    _log.Write("[CommunityChat] POST uncertain response: unrecognized envelope (no retry)");
                    return ProtocolErrorSend((int)response.StatusCode);
                }

                // 4) Dodatnie potwierdzenie: success=true albo faktyczny ID wpisu.
                if (envelopeOk.Success != true && string.IsNullOrWhiteSpace(envelopeOk.EntryId))
                {
                    _log.Write("[CommunityChat] POST uncertain response: no success flag and no id (no retry)");
                    return ProtocolErrorSend((int)response.StatusCode);
                }

                // The server's persisted snapshot is authoritative, including author, time and permissions.
                var message = ParseEntry(envelopeOk.Data ?? envelopeOk.Root);
                if (message?.IsComplete != true)
                {
                    _log.Write("[CommunityChat] POST uncertain response: incomplete message (no retry)");
                    return ProtocolErrorSend((int)response.StatusCode);
                }

                return new CommunityChatSendResult
                {
                    Status = CommunityChatStatus.Success,
                    Message = message,
                    ModerationWarning = envelopeOk.ModerationWarning ?? false,
                    HttpStatusCode = (int)response.StatusCode
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Write("[CommunityChat] POST anulowane");
                return new CommunityChatSendResult
                {
                    Status = CommunityChatStatus.Cancelled,
                    ErrorCode = CommunityChatErrorCodes.Cancelled,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.Cancelled)
                };
            }
            catch (Exception ex)
            {
                // Niepewna odpowiedź POST nie jest ponawiana automatycznie (ryzyko duplikatu).
                _log.Write($"[CommunityChat] POST exception: {ex.GetType().Name}");
                return new CommunityChatSendResult
                {
                    Status = CommunityChatStatus.NetworkError,
                    ErrorCode = CommunityChatErrorCodes.NetworkError,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.NetworkError)
                };
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Moderacja: usuwanie i zgłaszanie
        // ═══════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public async Task<CommunityChatModerationResult> DeleteMessageAsync(string messageId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.ValidationFailed,
                    ErrorCode = CommunityChatErrorCodes.RequestFailed,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.ValidationFailed)
                };
            }

            try
            {
                using var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Delete,
                    RelativePath = EntryPath(messageId),
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return BuildModerationFailure(response, body, "DELETE");

                var evaluated = EvaluateModerationResponse(response, body, "DELETE");
                if (!evaluated.IsSuccess)
                    return evaluated;

                _log.Write("[CommunityChat] DELETE ok");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.Success,
                    HttpStatusCode = (int)response.StatusCode
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Write("[CommunityChat] DELETE anulowane");
                return CancelledModeration();
            }
            catch (Exception ex)
            {
                _log.Write($"[CommunityChat] DELETE exception: {ex.GetType().Name}");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.NetworkError,
                    ErrorCode = CommunityChatErrorCodes.NetworkError,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.NetworkError)
                };
            }
        }

        /// <inheritdoc />
        public async Task<CommunityChatModerationResult> ReportMessageAsync(
            string messageId,
            string? reason = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.ValidationFailed,
                    ErrorCode = CommunityChatErrorCodes.RequestFailed,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.ValidationFailed)
                };
            }

            var effectiveReason = string.IsNullOrWhiteSpace(reason)
                ? CommunityChatConstants.DefaultReportReason
                : reason.Trim();

            var json = JsonSerializer.Serialize(new { reason = effectiveReason }, JsonOptions);
            using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var response = await _apiClient.SendAsync(new SusModderApiRequest
                {
                    Method = HttpMethod.Post,
                    RelativePath = $"{EntryPath(messageId)}/report",
                    Content = httpContent,
                    UserHash = _userHash,
                    IncludeAuthToken = true
                }, ct).ConfigureAwait(false);

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return BuildModerationFailure(response, body, "REPORT");

                // Zgłoszenie może zwrócić 200 z success=false — wtedy zgłoszenie nie zostało przyjęte.
                var evaluated = EvaluateModerationResponse(response, body, "REPORT");
                if (!evaluated.IsSuccess)
                    return evaluated;

                _log.Write("[CommunityChat] REPORT ok");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.Success,
                    HttpStatusCode = (int)response.StatusCode
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _log.Write("[CommunityChat] REPORT anulowane");
                return CancelledModeration();
            }
            catch (Exception ex)
            {
                _log.Write($"[CommunityChat] REPORT exception: {ex.GetType().Name}");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.NetworkError,
                    ErrorCode = CommunityChatErrorCodes.NetworkError,
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.NetworkError)
                };
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Pomocnicze
        // ═══════════════════════════════════════════════════════════════

        private static string EntryPath(string messageId) =>
            $"{CommunityChatConstants.ResourcePath}/{Uri.EscapeDataString(messageId)}";

        private CommunityChatModerationResult BuildModerationFailure(
            HttpResponseMessage response,
            string body,
            string operation)
        {
            var envelope = ParseEnvelope(body);
            var status = MapStatus(response.StatusCode);
            _log.Write($"[CommunityChat] {operation} failed: HTTP {(int)response.StatusCode} status={status} code={SafeLogToken(envelope.ErrorCode)}");

            return new CommunityChatModerationResult
            {
                Status = status,
                ErrorCode = ResolveErrorCode(envelope.ErrorCode, status),
                ErrorMessage = envelope.ErrorMessage ?? DefaultMessageFor(status),
                RetryAfterSeconds = ParseRetryAfter(response),
                HttpStatusCode = (int)response.StatusCode
            };
        }

        /// <summary>
        /// Ocena odpowiedzi 2xx dla operacji moderacyjnych (usunięcie, zgłoszenie).
        /// Sukces wymaga dodatniego potwierdzenia właściwego dla operacji:
        /// - wspólne potwierdzenie <c>success: true</c> (o ile nie ma jawnego "nie" dla operacji),
        /// - flaga operacji: <c>deleted</c>/<c>removed</c> dla DELETE, <c>reported</c> dla REPORT.
        /// Sam identyfikator wpisu NIE jest potwierdzeniem operacji moderacyjnej,
        /// a flaga innej operacji (np. "reported" w odpowiedzi na DELETE) nie potwierdza sukcesu.
        /// </summary>
        private CommunityChatModerationResult EvaluateModerationResponse(
            HttpResponseMessage response,
            string body,
            string operation)
        {
            // 204/205 No Content — brak treści jest tutaj potwierdzeniem wykonania operacji.
            if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.ResetContent)
            {
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.Success,
                    HttpStatusCode = (int)response.StatusCode
                };
            }

            var envelope = ParseEnvelope(body);

            if (!envelope.IsValidJson)
            {
                _log.Write($"[CommunityChat] {operation} uncertain response: malformed JSON (no retry)");
                return ProtocolErrorModeration((int)response.StatusCode);
            }

            if (envelope.HasFailure)
            {
                _log.Write($"[CommunityChat] {operation} rejected by server: code={SafeLogToken(envelope.ErrorCode)}");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.RequestFailed,
                    ErrorCode = ResolveErrorCode(envelope.ErrorCode, CommunityChatStatus.RequestFailed),
                    ErrorMessage = envelope.ErrorMessage ?? DefaultMessageFor(CommunityChatStatus.RequestFailed),
                    HttpStatusCode = (int)response.StatusCode
                };
            }

            var isDelete = string.Equals(operation, "DELETE", StringComparison.Ordinal);

            // Jawne "nie" dla flagi właściwej dla operacji (w korzeniu lub w "data")
            // oznacza, że operacja nie została wykonana — mimo success:true.
            var hasNegativeFlag = isDelete
                ? envelope.HasNegativeDeleted || envelope.HasNegativeRemoved
                : envelope.HasNegativeReported;

            if (hasNegativeFlag)
            {
                _log.Write($"[CommunityChat] {operation} not confirmed: explicit negative operation flag");
                return new CommunityChatModerationResult
                {
                    Status = CommunityChatStatus.RequestFailed,
                    ErrorCode = ResolveErrorCode(null, CommunityChatStatus.RequestFailed),
                    ErrorMessage = DefaultMessageFor(CommunityChatStatus.RequestFailed),
                    HttpStatusCode = (int)response.StatusCode
                };
            }

            // Potwierdzenie: success:true (wspólne) albo flaga właściwa dla operacji.
            // Sam identyfikator wpisu ani flaga innej operacji nie potwierdzają sukcesu.
            var hasOperationFlag = isDelete
                ? envelope.HasPositiveDeleted || envelope.HasPositiveRemoved
                : envelope.HasPositiveReported;

            var isAcknowledged = envelope.Success == true || hasOperationFlag;

            if (!envelope.IsRecognized || !isAcknowledged)
            {
                _log.Write($"[CommunityChat] {operation} uncertain response: no operation-specific confirmation (no retry)");
                return ProtocolErrorModeration((int)response.StatusCode);
            }

            return new CommunityChatModerationResult
            {
                Status = CommunityChatStatus.Success,
                HttpStatusCode = (int)response.StatusCode
            };
        }

        private static CommunityChatMessagesResult ProtocolErrorMessages(int statusCode) => new()
        {
            Status = CommunityChatStatus.ProtocolError,
            ErrorCode = CommunityChatErrorCodes.ProtocolError,
            ErrorMessage = DefaultMessageFor(CommunityChatStatus.ProtocolError),
            HttpStatusCode = statusCode
        };

        private static CommunityChatSendResult ProtocolErrorSend(int statusCode) => new()
        {
            Status = CommunityChatStatus.ProtocolError,
            ErrorCode = CommunityChatErrorCodes.ProtocolError,
            ErrorMessage = DefaultMessageFor(CommunityChatStatus.ProtocolError),
            HttpStatusCode = statusCode
        };

        private static CommunityChatModerationResult ProtocolErrorModeration(int statusCode) => new()
        {
            Status = CommunityChatStatus.ProtocolError,
            ErrorCode = CommunityChatErrorCodes.ProtocolError,
            ErrorMessage = DefaultMessageFor(CommunityChatStatus.ProtocolError),
            HttpStatusCode = statusCode
        };

        private static CommunityChatMessagesResult CancelledMessages() => new()
        {
            Status = CommunityChatStatus.Cancelled,
            ErrorCode = CommunityChatErrorCodes.Cancelled,
            ErrorMessage = DefaultMessageFor(CommunityChatStatus.Cancelled)
        };

        private static CommunityChatModerationResult CancelledModeration() => new()
        {
            Status = CommunityChatStatus.Cancelled,
            ErrorCode = CommunityChatErrorCodes.Cancelled,
            ErrorMessage = DefaultMessageFor(CommunityChatStatus.Cancelled)
        };

        private static CommunityChatStatus MapStatus(HttpStatusCode statusCode) => (int)statusCode switch
        {
            401 => CommunityChatStatus.Unauthorized,
            403 => CommunityChatStatus.Forbidden,
            404 => CommunityChatStatus.NotFound,
            429 => CommunityChatStatus.RateLimited,
            >= 500 => CommunityChatStatus.ServerError,
            >= 400 => CommunityChatStatus.RequestFailed,
            _ => CommunityChatStatus.UnknownError
        };

        private static string DefaultCodeFor(CommunityChatStatus status) => status switch
        {
            CommunityChatStatus.Unauthorized => CommunityChatErrorCodes.Unauthorized,
            CommunityChatStatus.Forbidden => CommunityChatErrorCodes.Forbidden,
            CommunityChatStatus.RateLimited => CommunityChatErrorCodes.RateLimited,
            CommunityChatStatus.ServerError => CommunityChatErrorCodes.ServerError,
            CommunityChatStatus.NetworkError => CommunityChatErrorCodes.NetworkError,
            CommunityChatStatus.ProtocolError => CommunityChatErrorCodes.ProtocolError,
            CommunityChatStatus.Cancelled => CommunityChatErrorCodes.Cancelled,
            CommunityChatStatus.NotFound => CommunityChatErrorCodes.NotFound,
            CommunityChatStatus.RequestFailed => CommunityChatErrorCodes.RequestFailed,
            _ => CommunityChatErrorCodes.UnknownError
        };

        private static string DefaultMessageFor(CommunityChatStatus status) => status switch
        {
            CommunityChatStatus.ValidationFailed => "Message content rejected by client validation.",
            CommunityChatStatus.Unauthorized => "Missing or invalid authentication token.",
            CommunityChatStatus.Forbidden => "Operation not permitted.",
            CommunityChatStatus.RateLimited => "Too many requests. Retry later.",
            CommunityChatStatus.ServerError => "Server error. Try again later.",
            CommunityChatStatus.NetworkError => "Network unavailable.",
            CommunityChatStatus.ProtocolError => "Unexpected server response.",
            CommunityChatStatus.Cancelled => "Operation cancelled.",
            CommunityChatStatus.NotFound => "Message not found.",
            CommunityChatStatus.RequestFailed => "Request rejected by server.",
            _ => "Unknown error."
        };

        private static int? ParseRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter is not null)
            {
                if (retryAfter.Delta is { } delta)
                    return (int)Math.Ceiling(Math.Max(0, delta.TotalSeconds));

                if (retryAfter.Date is { } date)
                {
                    var seconds = (date - DateTimeOffset.UtcNow).TotalSeconds;
                    return (int)Math.Ceiling(Math.Max(0, seconds));
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                foreach (var value in values)
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                        return Math.Max(0, seconds);
                }
            }

            return null;
        }

        // ── Parsowanie odpowiedzi ────────────────────────────────────────────

        /// <summary>
        /// Rozbija odpowiedź API na bezpieczną, znormalizowaną strukturę.
        /// Kody i komunikaty pochodzące z odpowiedzi są traktowane jako dane nieufne:
        /// kod jest dopuszczany tylko wtedy, gdy pasuje do bezpiecznego wzorca,
        /// komunikat jest oczyszczany z znaków sterujących i skracany.
        /// </summary>
        private static ChatEnvelope ParseEnvelope(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new ChatEnvelope { IsValidJson = false };

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return new ChatEnvelope { IsValidJson = false };
            }

            using (document)
            {
                var root = document.RootElement.Clone();

                JsonElement? data = null;
                if (root.ValueKind == JsonValueKind.Object &&
                    TryGetProperty(root, "data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.Object)
                {
                    data = dataElement.Clone();
                }

                // Flaga sukcesu: root + zagnieżdżone "data". Fałsz w dowolnym miejscu oznacza porażkę.
                bool? success = null;
                var rootSuccess = ReadBool(root, "success");
                if (rootSuccess.HasValue)
                    success = rootSuccess;

                if (data is { } dataObject)
                {
                    var nestedSuccess = ReadBool(dataObject, "success");
                    if (nestedSuccess == false)
                        success = false;
                    else if (nestedSuccess.HasValue && success is null)
                        success = nestedSuccess;
                }

                string? errorCode = null;
                string? errorMessage = null;
                var hasFailure = success == false;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetProperty(root, "error", out var errorElement) && errorElement.ValueKind != JsonValueKind.Null)
                    {
                        hasFailure = true;
                        if (errorElement.ValueKind == JsonValueKind.String)
                        {
                            errorCode = NormalizeErrorCode(errorElement.GetString());
                        }
                        else
                        {
                            errorCode = NormalizeErrorCode(ReadString(errorElement, "code") ?? ReadString(errorElement, "errorCode"));
                            errorMessage = SanitizeText(ReadString(errorElement, "message") ?? ReadString(errorElement, "detail"));
                        }
                    }

                    var rootErrorCode = ReadString(root, "errorCode");
                    if (!string.IsNullOrWhiteSpace(rootErrorCode))
                    {
                        hasFailure = true;
                        errorCode ??= NormalizeErrorCode(rootErrorCode);
                        errorMessage ??= SanitizeText(ReadString(root, "message"));
                    }

                    if (success == false)
                    {
                        errorCode ??= NormalizeErrorCode(ReadString(root, "code"));
                        errorMessage ??= SanitizeText(ReadString(root, "message"));
                    }
                }

                if (data is { } nested)
                {
                    if (TryGetProperty(nested, "error", out var nestedError) && nestedError.ValueKind != JsonValueKind.Null)
                    {
                        hasFailure = true;
                        if (nestedError.ValueKind == JsonValueKind.String)
                        {
                            errorCode ??= NormalizeErrorCode(nestedError.GetString());
                        }
                        else
                        {
                            errorCode ??= NormalizeErrorCode(ReadString(nestedError, "code") ?? ReadString(nestedError, "errorCode"));
                            errorMessage ??= SanitizeText(ReadString(nestedError, "message"));
                        }
                    }

                    var nestedErrorCode = ReadString(nested, "errorCode");
                    if (!string.IsNullOrWhiteSpace(nestedErrorCode))
                    {
                        hasFailure = true;
                        errorCode ??= NormalizeErrorCode(nestedErrorCode);
                        errorMessage ??= SanitizeText(ReadString(nested, "message"));
                    }
                }

                var entryId = ReadEntryId(root, data);
                var moderationWarning = ReadBool(root, "moderationWarning")
                    ?? (data is { } warningHolder ? ReadBool(warningHolder, "moderationWarning") : null);
                var expiresAt = ReadTimestamp(root, "expiresAt")
                    ?? (data is { } expiresHolder ? ReadTimestamp(expiresHolder, "expiresAt") : null);

                return new ChatEnvelope
                {
                    IsValidJson = true,
                    Root = root,
                    Data = data,
                    Success = success,
                    HasFailure = hasFailure,
                    ErrorCode = errorCode,
                    ErrorMessage = errorMessage,
                    EntryId = entryId,
                    ModerationWarning = moderationWarning,
                    ExpiresAt = expiresAt,
                    RootDeleted = ReadBool(root, "deleted"),
                    NestedDeleted = data is { } nestedDeleted ? ReadBool(nestedDeleted, "deleted") : null,
                    RootRemoved = ReadBool(root, "removed"),
                    NestedRemoved = data is { } nestedRemoved ? ReadBool(nestedRemoved, "removed") : null,
                    RootReported = ReadBool(root, "reported"),
                    NestedReported = data is { } nestedReported ? ReadBool(nestedReported, "reported") : null,
                    IsRecognized = LooksLikeKnownEnvelope(root, data)
                };
            }
        }

        /// <summary>
        /// Dopuszcza kod błędu tylko w bezpiecznym wzorcu (ASCII, litery/cyfry/underscore, do 64 znaków).
        /// Wszystko inne jest odrzucane — kod nigdy nie zawiera znaków sterujących ani treści z serwera.
        /// </summary>
        private static string? NormalizeErrorCode(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var trimmed = raw.Trim();
            if (trimmed.Length > 64)
                return null;

            if (!char.IsAsciiLetter(trimmed[0]))
                return null;

            foreach (var c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                    return null;
            }

            return trimmed.ToUpperInvariant();
        }

        /// <summary>
        /// Kod błędu dopuszczony do zapisu w logu — wyłącznie z białej listy.
        /// Pozostałe wartości są zapisywane jako "UNKNOWN" (do logu nie trafia surowe pole odpowiedzi).
        /// </summary>
        private static string SafeLogToken(string? code) =>
            code is not null && KnownSafeErrorCodes.Contains(code) ? code : "UNKNOWN";

        /// <summary>
        /// Kod błędu przekazywany do UI. Zwracany jest wyłącznie kod znany i bezpieczny
        /// (biała listka); kod z odpowiedzi serwera spoza listy jest zastępowany kodem domyślnym
        /// dla danego statusu — UI nigdy nie musi obsługiwać dowolnych ciągów z serwera.
        /// </summary>
        private static string ResolveErrorCode(string? rawCode, CommunityChatStatus status)
        {
            var normalized = NormalizeErrorCode(rawCode);
            if (normalized is not null && KnownSafeErrorCodes.Contains(normalized))
                return normalized;

            return DefaultCodeFor(status);
        }

        /// <summary>Usuwa znaki sterujące i skraca komunikat techniczny.</summary>
        private static string? SanitizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (!char.IsControl(c))
                    builder.Append(c);
            }

            var sanitized = builder.ToString().Trim();
            if (sanitized.Length == 0)
                return null;

            return sanitized.Length > 200 ? sanitized[..200] : sanitized;
        }

        private static bool LooksLikeKnownEnvelope(JsonElement root, JsonElement? data)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return true;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var key in KnownEnvelopeKeys)
            {
                if (TryGetProperty(root, key, out _))
                    return true;
            }

            if (data is { } dataObject)
            {
                foreach (var key in KnownEnvelopeKeys)
                {
                    if (TryGetProperty(dataObject, key, out _))
                        return true;
                }
            }

            return false;
        }

        private static string? ReadEntryId(JsonElement root, JsonElement? data)
        {
            var id = ReadString(root, "id") ?? ReadString(root, "entryId") ?? ReadString(root, "messageId");
            if (!string.IsNullOrWhiteSpace(id))
                return id;

            if (data is { } dataObject)
            {
                id = ReadString(dataObject, "id") ?? ReadString(dataObject, "entryId") ?? ReadString(dataObject, "messageId");
            }

            return string.IsNullOrWhiteSpace(id) ? null : id;
        }

        private List<CommunityChatMessage>? ParseEntries(
            ChatEnvelope envelope,
            out string? errorCode,
            out string? errorMessage)
        {
            errorCode = null;
            errorMessage = null;

            var root = envelope.Root;
            var data = envelope.Data;

            JsonElement? array = null;

            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in EntryArrayNames)
                {
                    if (TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.Array)
                    {
                        array = element;
                        break;
                    }
                }

                if (array is null && TryGetProperty(root, "data", out var rawData))
                {
                    if (rawData.ValueKind == JsonValueKind.Array)
                    {
                        array = rawData;
                    }
                    else if (rawData.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var name in EntryArrayNames)
                        {
                            if (TryGetProperty(rawData, name, out var element) && element.ValueKind == JsonValueKind.Array)
                            {
                                array = element;
                                break;
                            }
                        }
                    }
                }
            }

            if (array is null)
            {
                errorCode = CommunityChatErrorCodes.ProtocolError;
                errorMessage = "Unexpected response envelope.";
                return null;
            }

            var messages = new List<CommunityChatMessage>();
            var malformedCount = 0;

            foreach (var element in array.Value.EnumerateArray())
            {
                var message = ParseEntry(element);
                if (message?.IsComplete == true)
                {
                    messages.Add(message);
                    continue;
                }

                // Wpis o innym typie (np. legacy "code") jest odfiltrowany świadomie —
                // nie jest błędem protokołu. Brak id/content to już wadliwy wpis.
                if (!IsForeignEntryType(element))
                    malformedCount++;
            }

            // An incomplete snapshot must not erase existing UI history or masquerade as an empty chat.
            if (malformedCount > 0)
            {
                errorCode = CommunityChatErrorCodes.ProtocolError;
                errorMessage = "Malformed entries in response.";
                return null;
            }

            return messages;
        }

        /// <summary>Czy wpis ma typ inny niż "message" (np. legacy wpis z kodem lobby).</summary>
        private static bool IsForeignEntryType(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return false;

            var type = ReadString(element, "type");
            return type is not null
                && !type.Equals(CommunityChatConstants.EntryType, StringComparison.OrdinalIgnoreCase);
        }

        private CommunityChatMessage? ParseEntry(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            var id = ReadString(element, "id") ?? ReadString(element, "entryId") ?? ReadString(element, "messageId");
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var content = ReadString(element, "content") ?? ReadString(element, "text") ?? ReadString(element, "body");
            if (content is null)
                return null;

            var type = ReadString(element, "type");
            if (type is not null && !type.Equals(CommunityChatConstants.EntryType, StringComparison.OrdinalIgnoreCase))
                return null;

            // Never infer permission from the author ID, nickname, or a locally sent message ID.
            var isOwn = ReadBool(element, "isOwn") ?? ReadBool(element, "own") ?? false;
            var canDelete = ReadBool(element, "canDelete") ?? false;

            return new CommunityChatMessage
            {
                Id = id,
                Content = content,
                PublishedAt = ReadTimestamp(element, "publishedAt") ?? ReadTimestamp(element, "createdAt"),
                ExpiresAt = ReadTimestamp(element, "expiresAt"),
                IsOwn = isOwn,
                CanDelete = canDelete,
                Author = ReadAuthor(element)
            };
        }

        private static CommunityChatAuthor? ReadAuthor(JsonElement element)
        {
            if (!TryGetProperty(element, "author", out var author) || author.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(author, "nickname", out var nickname) || nickname.ValueKind != JsonValueKind.Object)
                return null;
            var id = ReadAuthorString(author, "id");
            var pl = ReadAuthorString(nickname, "pl");
            var en = ReadAuthorString(nickname, "en");
            if (string.IsNullOrWhiteSpace(id) || !IsSafeNickname(pl) || !IsSafeNickname(en)) return null;
            return new CommunityChatAuthor(id, new CommunityChatNickname(pl!, en!));
        }

        private static string? ReadAuthorString(JsonElement element, string field) =>
            TryGetProperty(element, field, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        private static bool IsSafeNickname(string? name) => !string.IsNullOrWhiteSpace(name) &&
            !name.Any(c => char.IsControl(c) || c is '\u061c' or '\u200e' or '\u200f' or
                >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069');

        /// <summary>Deduplikacja po ID + porządek chronologiczny (najstarsze na początku).</summary>
        private static List<CommunityChatMessage> NormalizeMessages(List<CommunityChatMessage> messages)
        {
            var byId = new Dictionary<string, CommunityChatMessage>(StringComparer.Ordinal);

            foreach (var message in messages)
            {
                if (!byId.TryGetValue(message.Id, out var existing))
                {
                    byId[message.Id] = message;
                    continue;
                }

                // Duplikat ID — zachowujemy wersję z uzupełnionym czasem publikacji.
                if (existing.PublishedAt is null && message.PublishedAt is not null)
                    byId[message.Id] = message with { PublishedAt = message.PublishedAt };
            }

            var result = byId.Values.ToList();
            result.Sort(static (left, right) =>
            {
                var leftTime = left.PublishedAt?.UtcDateTime ?? DateTimeOffset.MaxValue.UtcDateTime;
                var rightTime = right.PublishedAt?.UtcDateTime ?? DateTimeOffset.MaxValue.UtcDateTime;

                var comparison = leftTime.CompareTo(rightTime);
                return comparison != 0
                    ? comparison
                    : string.CompareOrdinal(left.Id, right.Id);
            });

            return result;
        }

        /// <summary>Odrzuca wiadomości po terminie wygaśnięcia (TTL).</summary>
        private static List<CommunityChatMessage> FilterExpiredMessages(
            List<CommunityChatMessage> messages,
            DateTimeOffset now)
        {
            if (messages.Count == 0)
                return messages;

            var result = new List<CommunityChatMessage>(messages.Count);
            foreach (var message in messages)
            {
                if (message.ExpiresAt is { } expires && expires <= now)
                    continue;

                result.Add(message);
            }

            return result;
        }

        /// <summary>Trzyma najwyżej <paramref name="limit"/> najnowszych wiadomości (lista posortowana rosnąco).</summary>
        private static List<CommunityChatMessage> ClampMessages(List<CommunityChatMessage> messages, int limit)
        {
            if (limit <= 0)
                return new List<CommunityChatMessage>();

            if (messages.Count <= limit)
                return messages;

            return messages.GetRange(messages.Count - limit, limit);
        }

        /// <summary>
        /// Odczyt pola obiektu JSON. Najpierw dopasowanie dokładne, potem fallback
        /// bez rozróżniania wielkości liter — zgodnie z legacy (JsonSerializer z
        /// PropertyNameCaseInsensitive = true). Dzięki temu odpowiedzi backendu z polami
        /// PascalCase (np. "Id", "Content", "CreatedAt", "IsOwn") nie są cicho pomijane.
        /// </summary>
        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                value = default;
                return false;
            }

            if (element.TryGetProperty(name, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? ReadString(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.ToString(),
                _ => null
            };
        }

        private static bool? ReadBool(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) ? parsed : null,
                _ => null
            };
        }

        /// <summary>Czytanie znacznika czasu (ISO-8601 lub znacznik uniksowy).</summary>
        private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var property) || property.ValueKind == JsonValueKind.Null)
                return null;

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;

                return DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed)
                    ? parsed
                    : null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var unix))
            {
                return unix > 100_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            }

            return null;
        }

        /// <summary>Znormalizowana, bezpieczna reprezentacja odpowiedzi API.</summary>
        private sealed class ChatEnvelope
        {
            /// <summary>Czy odpowiedź jest poprawnym JSON-em.</summary>
            public bool IsValidJson { get; init; }

            /// <summary>Korzeń odpowiedzi (sklonowany, niezależny od dokumentu).</summary>
            public JsonElement Root { get; init; }

            /// <summary>Zagnieżdżony obiekt "data" (jeżeli istnieje).</summary>
            public JsonElement? Data { get; init; }

            /// <summary>Łączna flaga sukcesu (root + data); null gdy nie występuje.</summary>
            public bool? Success { get; init; }

            /// <summary>Czy odpowiedź niesie sygnał porażki (success=false lub obiekt "error").</summary>
            public bool HasFailure { get; init; }

            /// <summary>Znormalizowany kod błędu (bezpieczny wzorzec) lub null.</summary>
            public string? ErrorCode { get; init; }

            /// <summary>Oczyszczony komunikat techniczny lub null.</summary>
            public string? ErrorMessage { get; init; }

            /// <summary>Identyfikator utworzonego wpisu (jeżeli serwer go zwrócił).</summary>
            public string? EntryId { get; init; }

            /// <summary>Flaga ostrzeżenia moderacji.</summary>
            public bool? ModerationWarning { get; init; }

            /// <summary>Czas wygaśnięcia utworzonego wpisu.</summary>
            public DateTimeOffset? ExpiresAt { get; init; }

            /// <summary>
            /// Flaga "deleted" w korzeniu odpowiedzi (null, gdy jej nie ma).
            /// </summary>
            public bool? RootDeleted { get; init; }

            /// <summary>Flaga "deleted" w zagnieżdżonym obiekcie "data".</summary>
            public bool? NestedDeleted { get; init; }

            /// <summary>Flaga "removed" w korzeniu odpowiedzi.</summary>
            public bool? RootRemoved { get; init; }

            /// <summary>Flaga "removed" w zagnieżdżonym obiekcie "data".</summary>
            public bool? NestedRemoved { get; init; }

            /// <summary>Flaga "reported" w korzeniu odpowiedzi.</summary>
            public bool? RootReported { get; init; }

            /// <summary>Flaga "reported" w zagnieżdżonym obiekcie "data".</summary>
            public bool? NestedReported { get; init; }

            /// <summary>Czy odpowiedź jawnie zaprzecza usunięciu wpisu (w korzeniu lub w "data").</summary>
            public bool HasNegativeDeleted => RootDeleted == false || NestedDeleted == false;

            /// <summary>Czy odpowiedź jawnie zaprzecza usunięciu wpisu (flaga "removed").</summary>
            public bool HasNegativeRemoved => RootRemoved == false || NestedRemoved == false;

            /// <summary>Czy odpowiedź jawnie zaprzecza przyjęciu zgłoszenia.</summary>
            public bool HasNegativeReported => RootReported == false || NestedReported == false;

            /// <summary>Czy odpowiedź potwierdza usunięcie wpisu.</summary>
            public bool HasPositiveDeleted => RootDeleted == true || NestedDeleted == true;

            /// <summary>Czy odpowiedź potwierdza usunięcie wpisu (flaga "removed").</summary>
            public bool HasPositiveRemoved => RootRemoved == true || NestedRemoved == true;

            /// <summary>Czy odpowiedź potwierdza przyjęcie zgłoszenia.</summary>
            public bool HasPositiveReported => RootReported == true || NestedReported == true;

            /// <summary>Czy odpowiedź ma rozpoznawalną umowę (a nie np. "{}" lub HTML).</summary>
            public bool IsRecognized { get; init; }
        }
    }
}
