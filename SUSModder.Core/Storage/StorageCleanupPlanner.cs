using SUSModder.Core.GameIntegration;

namespace SUSModder.Core.Storage;

/// <summary>Wejście planowania sprzątania.</summary>
public sealed record StorageCleanupPlanRequest
{
    /// <summary>Kanoniczny katalog biblioteki modów (mods install path).</summary>
    public required string LibraryRoot { get; init; }

    /// <summary>
    /// Znane katalogi instalacji modów. Pozwalają odróżnić instalacje od danych nieklasyfikowanych.
    /// Pusta lista oznacza, że wszystkie katalogi poza cache/roboczymi traktujemy jako nieklasyfikowane.
    /// </summary>
    public IReadOnlyCollection<string> KnownInstallPaths { get; init; } = Array.Empty<string>();

    /// <summary>Czy dodatkowo zmierzyć cały katalog biblioteki (dokładna suma; kosztuje dodatkowy skan).</summary>
    public bool MeasureLibraryTotal { get; init; }

    /// <summary>Opcje pomiaru rozmiaru.</summary>
    public StorageMeasurementOptions? MeasurementOptions { get; init; }
}

/// <summary>
/// Planuje sprzątanie katalogu modów: inwentaryzuje zawartość i opisuje kwalifikację pozycji.
///
/// Stan 3.1-08: **planowanie jest wyłącznie pomiarowe**. Żadna pozycja nie jest uprawniona
/// do usunięcia (allowlista nie została zatwierdzona), a wykonawca niczego nie usuwa.
///
/// Zasady:
/// - planowanie niczego nie usuwa,
/// - katalogi instalacji, konfiguracje, logi, backupy, cache i pliki aktualizatora są chronione,
/// - katalogi robocze i archiwa vanilla są mierzone i opisywane, ale pozostają „tylko pomiar”,
/// - reparse pointy (junction/symlink) nie są nigdy mierzone ani podnoszone do korzenia pomiaru,
/// - błędy inwentaryzacji są jawne — plan ma własny stan (complete/partial/unavailable).
/// </summary>
public sealed class StorageCleanupPlanner : IDisposable
{
    private readonly IStorageSizeMeasurementService _measurement;
    private readonly IStorageFileSystemProbe _probe;
    private readonly bool _ownsMeasurement;

    /// <summary>Tworzy planer. Bez podanego serwisu używa własnej instancji pomiarowej.</summary>
    public StorageCleanupPlanner(IStorageSizeMeasurementService? measurement = null)
        : this(measurement, DefaultStorageFileSystemProbe.Instance)
    {
    }

    /// <summary>Wariant dla testów — pozwala podstawić deterministyczny probe systemu plików.</summary>
    internal StorageCleanupPlanner(IStorageSizeMeasurementService? measurement, IStorageFileSystemProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        if (measurement is null)
        {
            _measurement = new StorageSizeMeasurementService(StorageSizeMeasurementService.DefaultMaxConcurrency, null, probe);
            _ownsMeasurement = true;
        }
        else
        {
            _measurement = measurement;
            _ownsMeasurement = false;
        }

        _probe = probe;
    }

    /// <summary>Buduje zapytanie planowania z katalogu biblioteki i listy zainstalowanych modów.</summary>
    public static StorageCleanupPlanRequest CreateRequest(
        string libraryRoot,
        IEnumerable<string>? knownInstallPaths = null,
        bool measureLibraryTotal = false) => new()
    {
        LibraryRoot = libraryRoot,
        KnownInstallPaths = knownInstallPaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? (IReadOnlyCollection<string>)Array.Empty<string>(),
        MeasureLibraryTotal = measureLibraryTotal
    };

    /// <summary>
    /// Tworzy plan sprzątania. Nie usuwa żadnych plików — wyłącznie mierzy i opisuje.
    /// Enumeracja katalogów jest synchroniczna, dlatego plan powstaje poza wątkiem UI.
    /// </summary>
    public Task<StorageCleanupPlan> PlanAsync(
        StorageCleanupPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() => BuildPlan(request, cancellationToken), cancellationToken);
    }

    private async Task<StorageCleanupPlan> BuildPlan(StorageCleanupPlanRequest request, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var libraryRoot = StoragePathNormalizer.TryNormalize(request.LibraryRoot) ?? string.Empty;

        if (libraryRoot.Length == 0)
        {
            return EmptyPlan(request.LibraryRoot ?? string.Empty, generatedAt, StorageSizeState.Unavailable, StorageSizeErrorCode.PathEmpty);
        }

        if (!Directory.Exists(libraryRoot))
        {
            return EmptyPlan(libraryRoot, generatedAt, StorageSizeState.Unavailable, StorageSizeErrorCode.PathNotFound);
        }

        // Preflight kontraktu „no-follow”: biblioteka nie może być linkiem.
        // Inwentaryzacja korzenia musi nastąpić dopiero po tym sprawdzeniu, inaczej plan
        // policzyłby bajty obcego drzewa i raportował „Complete”.
        if (StorageLinkBoundary.IsLinkPath(libraryRoot, libraryRoot, out var libraryLink))
        {
            return EmptyPlan(
                libraryRoot,
                generatedAt,
                StorageSizeState.Unavailable,
                StorageSizeErrorCode.PathIsLink,
                $"link_path:{libraryLink}");
        }

        // Inwentaryzacja biblioteki — błąd odczytu nie jest cicho zamieniany na „0 B”.
        if (!TryEnumerate(libraryRoot, cancellationToken, out var libraryEntries, out var errorCode, out var diagnostic))
        {
            return EmptyPlan(libraryRoot, generatedAt, StorageSizeState.Unavailable, errorCode, diagnostic);
        }

        var knownInstalls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in request.KnownInstallPaths ?? Array.Empty<string>())
        {
            var normalized = StoragePathNormalizer.TryNormalize(path);
            if (!string.IsNullOrEmpty(normalized))
                knownInstalls.Add(normalized!);
        }

        var vanillaRoot = StoragePathNormalizer.TryNormalize(VanillaCacheService.GetVanillaRoot(libraryRoot));
        var drafts = new List<PlanEntryDraft>();
        long rootLevelBytes = 0;
        var planState = StorageSizeState.Complete;

        foreach (var entry in libraryEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributes = TryReadAttributes(entry);
            if (attributes is null)
            {
                // Nie wiemy, czy to link — nie liczymy go i nie zgadujemy. Plan jest niepełny.
                planState = StorageSizeState.Partial;
                continue;
            }

            if (attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            {
                // Junction/symlink — nigdy nie mierzymy i nie podnosimy do korzenia pomiaru.
                drafts.Add(new PlanEntryDraft
                {
                    Entry = new StorageCleanupEntry
                    {
                        Id = $"linked:{entry.FullName}",
                        Category = StorageCleanupCategory.Unclassified,
                        Path = entry.FullName,
                        OwnerRoot = libraryRoot,
                        Eligibility = StorageCleanupEligibility.Protected,
                        SkipReason = StorageCleanupSkipReason.PathIsLink,
                        ReasonCode = StorageCleanupRules.ReasonLinkedEntry,
                        SizeState = StorageSizeState.Unavailable,
                        SizeErrorCode = StorageSizeErrorCode.PathIsLink,
                        IsLink = true
                    },
                    SkipMeasurement = true
                });

                planState = StorageSizeState.Partial;
                continue;
            }

            if (attributes.Value.HasFlag(FileAttributes.Directory))
            {
                var described = DescribeDirectory(libraryRoot, vanillaRoot, entry.FullName, knownInstalls, cancellationToken);
                drafts.AddRange(described.Drafts);

                if (described.EnumerationFailed)
                    planState = StorageSizeState.Partial;

                continue;
            }

            // Plik w katalogu biblioteki — liczony do sumy, nie jest osobną kategorią.
            var length = TryReadFileLength(entry.FullName);
            if (length is null)
            {
                // Nie udało się odczytać rozmiaru — suma byłaby zaniżona bez ostrzeżenia.
                planState = StorageSizeState.Partial;
                continue;
            }

            rootLevelBytes += length.Value;
        }

        // Pomiar rozmiaru pozycji (ścieżka = jeden pomiar, deduplikacja w cache).
        var measured = await MeasureEntriesAsync(drafts, request, libraryRoot, cancellationToken).ConfigureAwait(false);

        var summaries = BuildCategorySummaries(measured);

        // Pomiar, który wyszedł niepełny lub niedostępny, obniża stan całego planu.
        if (planState == StorageSizeState.Complete
            && measured.Any(e => e.SizeState is StorageSizeState.Partial or StorageSizeState.Unavailable))
        {
            planState = StorageSizeState.Partial;
        }

        long measuredBytes = rootLevelBytes + measured.Sum(e => e.MeasuredBytes);
        var planErrorCode = StorageSizeErrorCode.None;

        if (request.MeasureLibraryTotal)
        {
            var libraryMeasurement = await _measurement
                .MeasureAsync(libraryRoot, WithTrustedRoot(request.MeasurementOptions, libraryRoot), cancellationToken)
                .ConfigureAwait(false);

            if (libraryMeasurement.HasValue)
                measuredBytes = libraryMeasurement.LogicalBytes;

            // Stan pomiaru całej biblioteki obniża stan planu — nie wolno raportować „Complete”,
            // gdy pomiar biblioteki był niepełny albo niedostępny.
            switch (libraryMeasurement.State)
            {
                case StorageSizeState.Unavailable:
                    planState = StorageSizeState.Unavailable;
                    planErrorCode = libraryMeasurement.ErrorCode;
                    break;

                case StorageSizeState.Partial when planState == StorageSizeState.Complete:
                    planState = StorageSizeState.Partial;
                    planErrorCode = libraryMeasurement.ErrorCode;
                    break;
            }
        }

        var (free, total) = ReadDriveSpace(libraryRoot);

        return new StorageCleanupPlan
        {
            LibraryRoot = libraryRoot,
            GeneratedAtUtc = generatedAt,
            Entries = measured,
            Categories = summaries,
            State = planState,
            ErrorCode = planState == StorageSizeState.Unavailable
                ? (planErrorCode == StorageSizeErrorCode.None ? StorageSizeErrorCode.IoError : planErrorCode)
                : StorageSizeErrorCode.None,
            MeasuredBytes = measuredBytes,
            // 3.1-08: allowlista nie została zatwierdzona — nic nie jest warte usunięcia.
            EstimatedRecoverableBytes = 0,
            FreeDiskSpaceBytes = free,
            TotalDiskSpaceBytes = total
        };
    }

    private PlanEntryDescription DescribeDirectory(
        string libraryRoot,
        string? vanillaRoot,
        string directory,
        IReadOnlyCollection<string> knownInstalls,
        CancellationToken cancellationToken)
    {
        var drafts = new List<PlanEntryDraft>();
        var enumerationFailed = false;
        var name = Path.GetFileName(directory);

        if (string.Equals(name, StorageCleanupRules.InstallWorkspaceDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            // Katalogi robocze: własność można rozpoznać (GUID od instalatorów),
            // ale brak aktywnego użycia NIE da się udowodnić (brak mechanizmu lease w Core).
            if (!TryEnumerate(directory, cancellationToken, out var workspaceEntries, out _, out _))
                enumerationFailed = true;

            foreach (var entry in workspaceEntries)
            {
                var attributes = TryReadAttributes(entry);
                if (attributes is null)
                {
                    enumerationFailed = true;
                    continue;
                }

                if (attributes.Value.HasFlag(FileAttributes.ReparsePoint))
                {
                    drafts.Add(LinkedDraft(entry.FullName, directory));
                    enumerationFailed = true;
                    continue;
                }

                if (attributes.Value.HasFlag(FileAttributes.Directory))
                {
                    drafts.Add(new PlanEntryDraft
                    {
                        Entry = new StorageCleanupEntry
                        {
                            Id = $"install_workspace:{entry.Name}",
                            Category = StorageCleanupCategory.InstallWorkspace,
                            Path = entry.FullName,
                            OwnerRoot = directory,
                            Eligibility = StorageCleanupEligibility.MeasurementOnly,
                            SkipReason = StorageCleanupSkipReason.ActiveUseNotProvable,
                            ReasonCode = StorageCleanupRules.ReasonInstallWorkspaceActiveUseUnprovable
                        }
                    });

                    continue;
                }

                drafts.Add(new PlanEntryDraft
                {
                    Entry = new StorageCleanupEntry
                    {
                        Id = $"unclassified:{entry.FullName}",
                        Category = StorageCleanupCategory.Unclassified,
                        Path = entry.FullName,
                        OwnerRoot = directory,
                        Eligibility = StorageCleanupEligibility.Protected,
                        SkipReason = StorageCleanupSkipReason.ProtectedData,
                        ReasonCode = StorageCleanupRules.ReasonUnclassifiedData
                    }
                });
            }

            return new PlanEntryDescription(drafts, enumerationFailed);
        }

        if (!string.IsNullOrEmpty(vanillaRoot)
            && string.Equals(vanillaRoot, StoragePathNormalizer.TryNormalize(directory), StringComparison.OrdinalIgnoreCase))
        {
            var vanilla = DescribeVanillaRoot(directory, cancellationToken);
            return new PlanEntryDescription(vanilla.Drafts, vanilla.EnumerationFailed);
        }

        var normalizedDirectory = StoragePathNormalizer.TryNormalize(directory) ?? directory;

        // Instalacja moda: katalog instalacji albo jego rodzic (układ Epic: <mods>/<Mod>/AmongUs).
        var isInstallation = knownInstalls.Any(install =>
            StoragePathNormalizer.IsSameOrSubPath(install, normalizedDirectory)
            || StoragePathNormalizer.IsSameOrSubPath(normalizedDirectory, install));

        drafts.Add(new PlanEntryDraft
        {
            Entry = new StorageCleanupEntry
            {
                Id = isInstallation ? $"installation:{normalizedDirectory}" : $"unclassified:{normalizedDirectory}",
                Category = isInstallation ? StorageCleanupCategory.ModInstallations : StorageCleanupCategory.Unclassified,
                Path = normalizedDirectory,
                OwnerRoot = libraryRoot,
                Eligibility = StorageCleanupEligibility.Protected,
                SkipReason = StorageCleanupSkipReason.ProtectedData,
                ReasonCode = isInstallation
                    ? StorageCleanupRules.ReasonModInstallation
                    : StorageCleanupRules.ReasonUnclassifiedData
            }
        });

        return new PlanEntryDescription(drafts, enumerationFailed);
    }

    private PlanEntryDescription DescribeVanillaRoot(
        string vanillaRoot,
        CancellationToken cancellationToken)
    {
        var drafts = new List<PlanEntryDraft>();
        var enumerationFailed = false;

        if (!TryEnumerate(vanillaRoot, cancellationToken, out var entries, out _, out _))
            enumerationFailed = true;

        var extractedRoot = Path.Combine(vanillaRoot, StorageCleanupRules.VanillaExtractedDirectoryName);

        foreach (var entry in entries)
        {
            var attributes = TryReadAttributes(entry);
            if (attributes is null)
            {
                enumerationFailed = true;
                continue;
            }

            if (attributes.Value.HasFlag(FileAttributes.ReparsePoint))
            {
                drafts.Add(LinkedDraft(entry.FullName, vanillaRoot));
                enumerationFailed = true;
                continue;
            }

            if (attributes.Value.HasFlag(FileAttributes.Directory))
            {
                if (string.Equals(
                        StoragePathNormalizer.TryNormalize(entry.FullName),
                        StoragePathNormalizer.TryNormalize(extractedRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryEnumerate(entry.FullName, cancellationToken, out var versionEntries, out _, out _))
                        enumerationFailed = true;

                    foreach (var versionEntry in versionEntries)
                    {
                        var versionAttributes = TryReadAttributes(versionEntry);
                        if (versionAttributes is null)
                        {
                            enumerationFailed = true;
                            continue;
                        }

                        if (versionAttributes.Value.HasFlag(FileAttributes.ReparsePoint))
                        {
                            drafts.Add(LinkedDraft(versionEntry.FullName, vanillaRoot));
                            enumerationFailed = true;
                            continue;
                        }

                        if (!versionAttributes.Value.HasFlag(FileAttributes.Directory))
                        {
                            // Plik w katalogu rozpakowanego cache — nie jest katalogiem wersji.
                            // Traktujemy go jako dane nieklasyfikowane i zaznaczamy plan jako niepełny.
                            drafts.Add(UnclassifiedDraft(versionEntry.FullName, vanillaRoot));
                            enumerationFailed = true;
                            continue;
                        }

                        drafts.Add(new PlanEntryDraft
                        {
                            Entry = new StorageCleanupEntry
                            {
                                Id = $"vanilla_extracted:{versionEntry.Name}",
                                Category = StorageCleanupCategory.VanillaExtractedCache,
                                Path = versionEntry.FullName,
                                OwnerRoot = vanillaRoot,
                                Eligibility = StorageCleanupEligibility.Protected,
                                SkipReason = StorageCleanupSkipReason.ProtectedData,
                                ReasonCode = StorageCleanupRules.ReasonProtectedData
                            }
                        });
                    }

                    continue;
                }

                drafts.Add(new PlanEntryDraft
                {
                    Entry = new StorageCleanupEntry
                    {
                        Id = $"unclassified:{entry.FullName}",
                        Category = StorageCleanupCategory.Unclassified,
                        Path = entry.FullName,
                        OwnerRoot = vanillaRoot,
                        Eligibility = StorageCleanupEligibility.Protected,
                        SkipReason = StorageCleanupSkipReason.ProtectedData,
                        ReasonCode = StorageCleanupRules.ReasonUnclassifiedData
                    }
                });

                continue;
            }

            if (!entry.FullName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
            {
                drafts.Add(new PlanEntryDraft
                {
                    Entry = new StorageCleanupEntry
                    {
                        Id = $"unclassified:{entry.FullName}",
                        Category = StorageCleanupCategory.Unclassified,
                        Path = entry.FullName,
                        OwnerRoot = vanillaRoot,
                        Eligibility = StorageCleanupEligibility.Protected,
                        SkipReason = StorageCleanupSkipReason.ProtectedData,
                        ReasonCode = StorageCleanupRules.ReasonUnclassifiedData
                    }
                });

                continue;
            }

            // Archiwum vanilla — kwalifikacja informacyjna, uprawnienie pozostaje „tylko pomiar”.
            var qualifies = StorageCleanupRules.TryQualifyVanillaArchive(vanillaRoot, entry.FullName, out var qualification);
            var storageVersion = qualification?.StorageVersion ?? Path.GetFileNameWithoutExtension(entry.FullName);

            drafts.Add(new PlanEntryDraft
            {
                Entry = new StorageCleanupEntry
                {
                    Id = $"vanilla_archive:{storageVersion}",
                    Category = StorageCleanupCategory.VanillaArchive,
                    Path = StoragePathNormalizer.TryNormalize(entry.FullName) ?? entry.FullName,
                    OwnerRoot = vanillaRoot,
                    Eligibility = StorageCleanupEligibility.MeasurementOnly,
                    SkipReason = StorageCleanupSkipReason.SafetyNotProven,
                    ReasonCode = qualifies
                        ? StorageCleanupRules.ReasonVanillaArchiveRedundant
                        : StorageCleanupRules.ReasonVanillaArchiveCacheNotProven,
                    ObservedLength = qualification?.Length,
                    ObservedLastWriteTimeUtc = qualification?.LastWriteTimeUtc
                }
            });
        }

        return new PlanEntryDescription(drafts, enumerationFailed);
    }

    private static PlanEntryDraft UnclassifiedDraft(string path, string ownerRoot) => new()
    {
        Entry = new StorageCleanupEntry
        {
            Id = $"unclassified:{path}",
            Category = StorageCleanupCategory.Unclassified,
            Path = path,
            OwnerRoot = ownerRoot,
            Eligibility = StorageCleanupEligibility.Protected,
            SkipReason = StorageCleanupSkipReason.ProtectedData,
            ReasonCode = StorageCleanupRules.ReasonUnclassifiedData
        }
    };

    private static PlanEntryDraft LinkedDraft(string path, string ownerRoot) => new()
    {
        Entry = new StorageCleanupEntry
        {
            Id = $"linked:{path}",
            Category = StorageCleanupCategory.Unclassified,
            Path = path,
            OwnerRoot = ownerRoot,
            Eligibility = StorageCleanupEligibility.Protected,
            SkipReason = StorageCleanupSkipReason.PathIsLink,
            ReasonCode = StorageCleanupRules.ReasonLinkedEntry,
            SizeState = StorageSizeState.Unavailable,
            SizeErrorCode = StorageSizeErrorCode.PathIsLink,
            IsLink = true
        },
        SkipMeasurement = true
    };

    private async Task<List<StorageCleanupEntry>> MeasureEntriesAsync(
        List<PlanEntryDraft> drafts,
        StorageCleanupPlanRequest request,
        string libraryRoot,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
            return new List<StorageCleanupEntry>();

        var options = WithTrustedRoot(request.MeasurementOptions, libraryRoot);

        var tasks = drafts.Select(async draft =>
        {
            if (draft.SkipMeasurement)
                return draft.Entry;

            var result = await _measurement
                .MeasureAsync(draft.Entry.Path, options, cancellationToken)
                .ConfigureAwait(false);

            return draft.Entry with
            {
                MeasuredBytes = result.HasValue ? result.LogicalBytes : 0,
                SizeState = result.State,
                SizeErrorCode = result.ErrorCode
            };
        }).ToList();

        return (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();
    }

    private static StorageMeasurementOptions WithTrustedRoot(StorageMeasurementOptions? options, string libraryRoot)
    {
        var baseOptions = options ?? StorageMeasurementOptions.Default;
        return baseOptions with { TrustedRoot = libraryRoot };
    }

    private static List<StorageCleanupCategorySummary> BuildCategorySummaries(List<StorageCleanupEntry> entries)
    {
        var summaries = new List<StorageCleanupCategorySummary>();

        foreach (var group in entries.GroupBy(e => e.Category))
        {
            var eligibility = group.Any(e => e.Eligibility == StorageCleanupEligibility.Allowlisted)
                ? StorageCleanupEligibility.Allowlisted
                : group.Any(e => e.Eligibility == StorageCleanupEligibility.MeasurementOnly)
                    ? StorageCleanupEligibility.MeasurementOnly
                    : StorageCleanupEligibility.Protected;

            summaries.Add(new StorageCleanupCategorySummary
            {
                Category = group.Key,
                EntryCount = group.Count(),
                MeasuredBytes = group.Sum(e => e.MeasuredBytes),
                RecoverableBytes = group
                    .Where(e => e.Eligibility == StorageCleanupEligibility.Allowlisted)
                    .Sum(e => e.MeasuredBytes),
                Eligibility = eligibility
            });
        }

        return summaries
            .OrderBy(s => (int)s.Category)
            .ToList();
    }

    private bool TryEnumerate(
        string path,
        CancellationToken cancellationToken,
        out IReadOnlyList<FileSystemInfo> entries,
        out StorageSizeErrorCode errorCode,
        out string? diagnostic)
    {
        entries = Array.Empty<FileSystemInfo>();
        errorCode = StorageSizeErrorCode.None;
        diagnostic = null;

        try
        {
            var collected = new List<FileSystemInfo>();

            foreach (var entry in _probe.EnumerateEntries(path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                collected.Add(entry);
            }

            entries = collected;
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            errorCode = StorageSizeErrorCode.AccessDenied;
            diagnostic = ex.Message;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            errorCode = StorageSizeErrorCode.PathNotFound;
            diagnostic = "directory_missing";
            return false;
        }
        catch (IOException ex)
        {
            errorCode = StorageSizeErrorCode.IoError;
            diagnostic = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Odczytuje atrybuty wpisu. Null oznacza, że odczyt się nie udał — wtedy nie wolno
    /// zakładać, że wpis nie jest linkiem.
    /// </summary>
    private FileAttributes? TryReadAttributes(FileSystemInfo entry)
    {
        try
        {
            return _probe.GetAttributes(entry);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Odczytuje rozmiar pliku. Null oznacza, że odczyt się nie udał — wtedy suma
    /// byłaby zaniżona bez ostrzeżenia, więc plan musi być oznaczony jako niepełny.
    /// </summary>
    private long? TryReadFileLength(string path)
    {
        try
        {
            return _probe.GetFileLength(new FileInfo(path));
        }
        catch
        {
            return null;
        }
    }

    private static StorageCleanupPlan EmptyPlan(
        string libraryRoot,
        DateTimeOffset generatedAt,
        StorageSizeState state,
        StorageSizeErrorCode errorCode,
        string? diagnostic = null) => new()
    {
        LibraryRoot = libraryRoot,
        GeneratedAtUtc = generatedAt,
        Entries = Array.Empty<StorageCleanupEntry>(),
        Categories = Array.Empty<StorageCleanupCategorySummary>(),
        State = state,
        ErrorCode = errorCode,
        MeasuredBytes = 0,
        EstimatedRecoverableBytes = 0,
        DiagnosticMessage = diagnostic
    };

    private static (long? Free, long? Total) ReadDriveSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return (null, null);

            var drive = new DriveInfo(root);
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>Zwalnia własną instancję serwisu pomiaru (gdy nie podano własnej).</summary>
    public void Dispose()
    {
        if (_ownsMeasurement && _measurement is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed class PlanEntryDraft
    {
        public required StorageCleanupEntry Entry { get; init; }

        /// <summary>Wpisy linków nie są mierzone — pomiar linku oznaczałby skan obcego drzewa.</summary>
        public bool SkipMeasurement { get; init; }
    }

    private readonly record struct PlanEntryDescription(List<PlanEntryDraft> Drafts, bool EnumerationFailed);
}
