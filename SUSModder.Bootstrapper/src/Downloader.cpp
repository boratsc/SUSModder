#include "Downloader.h"

#include "Win32Helpers.h"

#include <windows.h>

#include <winhttp.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <vector>

namespace bootstrapper {

namespace {

bool CrackUrl(const std::wstring& url, URL_COMPONENTS& components, std::vector<wchar_t>& hostBuffer, std::vector<wchar_t>& pathBuffer) {
    hostBuffer.assign(256, 0);
    pathBuffer.assign(2048, 0);

    ZeroMemory(&components, sizeof(components));
    components.dwStructSize = sizeof(components);
    components.lpszHostName = hostBuffer.data();
    components.dwHostNameLength = static_cast<DWORD>(hostBuffer.size());
    components.lpszUrlPath = pathBuffer.data();
    components.dwUrlPathLength = static_cast<DWORD>(pathBuffer.size());
    components.dwSchemeLength = static_cast<DWORD>(-1);

    return WinHttpCrackUrl(url.c_str(), 0, 0, &components) == TRUE;
}

} // namespace

bool Downloader::DownloadFile(const std::wstring& url, const std::wstring& destinationPath, const ProgressCallback& onProgress, std::wstring& errorMessage) const {
    if (onProgress) {
        onProgress(0);
    }

    URL_COMPONENTS components{};
    std::vector<wchar_t> hostBuffer;
    std::vector<wchar_t> pathBuffer;
    if (!CrackUrl(url, components, hostBuffer, pathBuffer)) {
        errorMessage = L"Nie udalo sie sparsowac adresu URL paczki.";
        return false;
    }

    const std::wstring host(components.lpszHostName, components.dwHostNameLength);
    std::wstring path(components.lpszUrlPath, components.dwUrlPathLength);
    if (path.empty()) {
        path = L"/";
    }

    const HINTERNET session = WinHttpOpen(L"SUSModderBootstrapper/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
        WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (session == nullptr) {
        errorMessage = L"Nie udalo sie otworzyc sesji HTTP.";
        return false;
    }

    const HINTERNET connection = WinHttpConnect(session, host.c_str(), components.nPort, 0);
    if (connection == nullptr) {
        WinHttpCloseHandle(session);
        errorMessage = L"Nie udalo sie polaczyc z serwerem pobierania.";
        return false;
    }

    const DWORD requestFlags = components.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0;
    const HINTERNET request = WinHttpOpenRequest(connection, L"GET", path.c_str(), nullptr,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, requestFlags);
    if (request == nullptr) {
        WinHttpCloseHandle(connection);
        WinHttpCloseHandle(session);
        errorMessage = L"Nie udalo sie utworzyc zadania HTTP.";
        return false;
    }

    bool success = false;
    std::ofstream output(std::filesystem::path(destinationPath), std::ios::binary | std::ios::trunc);
    if (!output.is_open()) {
        WinHttpCloseHandle(request);
        WinHttpCloseHandle(connection);
        WinHttpCloseHandle(session);
        errorMessage = L"Nie udalo sie utworzyc pliku docelowego dla pobierania.";
        return false;
    }

    if (WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) &&
        WinHttpReceiveResponse(request, nullptr)) {
        DWORD statusCode = 0;
        DWORD statusCodeSize = sizeof(statusCode);
        WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX,
            &statusCode, &statusCodeSize, WINHTTP_NO_HEADER_INDEX);

        if (statusCode >= 200 && statusCode < 300) {
            unsigned long long totalBytes = 0;
            wchar_t contentLengthBuffer[64] = {};
            DWORD contentLengthSize = sizeof(contentLengthBuffer);
            if (WinHttpQueryHeaders(request, WINHTTP_QUERY_CONTENT_LENGTH, WINHTTP_HEADER_NAME_BY_INDEX,
                &contentLengthBuffer, &contentLengthSize, WINHTTP_NO_HEADER_INDEX)) {
                totalBytes = _wcstoui64(contentLengthBuffer, nullptr, 10);
            }

            unsigned long long downloaded = 0;
            std::vector<char> buffer(64 * 1024);

            while (true) {
                DWORD availableBytes = 0;
                if (!WinHttpQueryDataAvailable(request, &availableBytes)) {
                    errorMessage = L"Nie udalo sie sprawdzic dostepnosci danych podczas pobierania.";
                    break;
                }

                if (availableBytes == 0) {
                    success = true;
                    break;
                }

                DWORD bytesRead = 0;
                const DWORD bufferSize = std::min<DWORD>(availableBytes, static_cast<DWORD>(buffer.size()));
                if (!WinHttpReadData(request, buffer.data(), bufferSize, &bytesRead)) {
                    errorMessage = L"Nie udalo sie odczytac danych z odpowiedzi HTTP.";
                    break;
                }

                output.write(buffer.data(), bytesRead);
                downloaded += bytesRead;

                if (onProgress) {
                    unsigned int percent = 0;
                    if (totalBytes > 0) {
                        percent = static_cast<unsigned int>((downloaded * 100ULL) / totalBytes);
                    }
                    onProgress(percent > 100 ? 100 : percent);
                }
            }
        } else {
            errorMessage = L"Serwer zwrocil nieoczekiwany kod HTTP podczas pobierania.";
        }
    } else {
        errorMessage = L"Nie udalo sie wyslac zadania pobierania lub odebrac odpowiedzi.";
    }

    output.close();
    WinHttpCloseHandle(request);
    WinHttpCloseHandle(connection);
    WinHttpCloseHandle(session);

    if (success && onProgress) {
        onProgress(100);
    }

    return success;
}

} // namespace bootstrapper
