# API Wersjonowania Modów - Dokumentacja dla Deweloperów C#

## 🎯 Szybki Start

Nowy endpoint do pobierania historii wersji modów. Istniejący endpoint `/susmodder-config` **działa bez zmian**.

---

## 📡 Endpointy

### 1. GET `/susmodder-config` (BEZ ZMIAN)

**Zwraca:** Najnowsze wersje wszystkich modów

```csharp
// Bez zmian - używaj jak dotychczas
var response = await httpClient.GetAsync("https://api.susmodder.app/susmodder-config");
var mods = await response.Content.ReadAsAsync<List<ModConfig>>();
```

---

### 2. GET `/susmodder-config-versions` (NOWY)

**Zwraca:** Historię wersji modów (wszystkie lub filtrowane po modId)

#### Przykład 1: Wszystkie wersje

```http
GET https://api.susmodder.app/susmodder-config-versions
```

**Response:**
```json
{
  "success": true,
  "count": 16,
  "versions": [
    {
      "VersionId": 3,
      "ModId": 1,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      "GitHubRepoOrLink": "https://github.com/...",
      "EpicGitHubRepoOrLink": "https://github.com/...",
      "CreatedAt": "2024-10-29T09:15:00.000Z",
      "CreatedBy": null,
      "Notes": "Version changed from 5.3.1 to 5.4.0"
    },
    ...
  ]
}
```

#### Przykład 2: Wersje konkretnego moda

```http
GET https://api.susmodder.app/susmodder-config-versions?modId=1
```

**Response:**
```json
{
  "success": true,
  "modId": 1,
  "count": 3,
  "versions": [
    {
      "VersionId": 3,
      "ModId": 1,
      "ModVersion": "5.4.0",
      "AmongVersion": "2024.10.29",
      ...
    },
    {
      "VersionId": 2,
      "ModId": 1,
      "ModVersion": "5.3.1",
      "AmongVersion": "2024.10.01",
      ...
    }
  ]
}
```

**Sortowanie:** Od najnowszych do najstarszych (CreatedAt DESC)

---

## 💻 Implementacja C#

### Modele Danych

```csharp
// Model dla pojedynczej wersji moda
public class ModVersionHistory
{
    public int VersionId { get; set; }
    public int ModId { get; set; }
    public string ModVersion { get; set; }
    public string AmongVersion { get; set; }
    public string GitHubRepoOrLink { get; set; }
    public string EpicGitHubRepoOrLink { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? Notes { get; set; }
}

// Response API
public class ModVersionsResponse
{
    public bool Success { get; set; }
    public int? ModId { get; set; }  // null jeśli wszystkie mody
    public int Count { get; set; }
    public List<ModVersionHistory> Versions { get; set; }
}
```

### Przykład 1: Pobierz wszystkie wersje

```csharp
public async Task<List<ModVersionHistory>> GetAllModVersions()
{
    using var httpClient = new HttpClient();
    var response = await httpClient.GetAsync("https://api.susmodder.app/susmodder-config-versions");
    
    if (!response.IsSuccessStatusCode)
        return null;
    
    var result = await response.Content.ReadAsAsync<ModVersionsResponse>();
    return result.Success ? result.Versions : null;
}
```

### Przykład 2: Pobierz wersje konkretnego moda

```csharp
public async Task<List<ModVersionHistory>> GetModVersionHistory(int modId)
{
    using var httpClient = new HttpClient();
    var response = await httpClient.GetAsync(
        $"https://api.susmodder.app/susmodder-config-versions?modId={modId}"
    );
    
    if (!response.IsSuccessStatusCode)
        return null;
    
    var result = await response.Content.ReadAsAsync<ModVersionsResponse>();
    return result.Success ? result.Versions : null;
}
```

### Przykład 3: Użycie w aplikacji

```csharp
// Pobierz historię Town of Us (modId = 1)
var touHistory = await GetModVersionHistory(1);

if (touHistory != null && touHistory.Any())
{
    // Najnowsza wersja
    var latest = touHistory.First();
    Console.WriteLine($"Najnowsza wersja: {latest.ModVersion} dla Among Us {latest.AmongVersion}");
    
    // Wszystkie wersje
    foreach (var version in touHistory)
    {
        Console.WriteLine($"- v{version.ModVersion} ({version.AmongVersion}) - {version.CreatedAt:yyyy-MM-dd}");
    }
}
```

### Przykład 4: Porównanie z obecną wersją

```csharp
public async Task<bool> IsNewerVersionAvailable(int modId, string currentVersion)
{
    var history = await GetModVersionHistory(modId);
    
    if (history == null || !history.Any())
        return false;
    
    var latest = history.First();
    return latest.ModVersion != currentVersion;
}

// Użycie
if (await IsNewerVersionAvailable(1, "5.3.1"))
{
    MessageBox.Show("Dostępna nowa wersja Town of Us!");
}
```

### Przykład 5: Pobierz link do konkretnej wersji

```csharp
public async Task<string> GetDownloadLinkForVersion(int modId, string version, bool isEpic = false)
{
    var history = await GetModVersionHistory(modId);
    
    var versionInfo = history?.FirstOrDefault(v => v.ModVersion == version);
    
    if (versionInfo == null)
        return null;
    
    return isEpic ? versionInfo.EpicGitHubRepoOrLink : versionInfo.GitHubRepoOrLink;
}

// Użycie
string downloadLink = await GetDownloadLinkForVersion(1, "5.3.1", isEpic: false);
```

---

## 🔑 Kluczowe Informacje

### Pola Wersjonowane (Historia)
Tylko te 4 pola są przechowywane w historii:
1. `ModVersion` - wersja moda (string: "5.3.1", "latest", "beta 1.0")
2. `AmongVersion` - wersja Among Us (string: "2024.10.29")
3. `GitHubRepoOrLink` - link GitHub (Steam)
4. `EpicGitHubRepoOrLink` - link GitHub (Epic Games)

### Pola NIE Wersjonowane
Te pola są tylko w `/susmodder-config` (tabela config):
- `ModName`
- `PngFileName`
- `Description`
- `ModType`
- `DllInstallPath`
- `InstallPath`

### Sortowanie
Wersje są sortowane od najnowszych do najstarszych (CreatedAt DESC)

### Unikalność
Kombinacja `(ModId, ModVersion, AmongVersion)` jest unikalna

---

## 🎨 Przykładowe Scenariusze UI

### Scenariusz 1: Dropdown z wersjami

```csharp
// Wypełnij combobox wersjami moda
var versions = await GetModVersionHistory(modId);
comboBoxVersions.DataSource = versions;
comboBoxVersions.DisplayMember = "ModVersion";
comboBoxVersions.ValueMember = "VersionId";

// Przy wyborze wersji
private void comboBoxVersions_SelectedIndexChanged(object sender, EventArgs e)
{
    var selected = (ModVersionHistory)comboBoxVersions.SelectedItem;
    labelAmongVersion.Text = $"Among Us: {selected.AmongVersion}";
    linkDownload.Text = selected.GitHubRepoOrLink;
}
```

### Scenariusz 2: Historia zmian w DataGridView

```csharp
var versions = await GetModVersionHistory(modId);

dataGridView1.DataSource = versions.Select(v => new
{
    Wersja = v.ModVersion,
    AmongUs = v.AmongVersion,
    Data = v.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
    Notatki = v.Notes
}).ToList();
```

### Scenariusz 3: Sprawdzanie aktualizacji przy starcie

```csharp
private async void MainForm_Load(object sender, EventArgs e)
{
    // Pobierz obecną konfigurację
    var config = await GetCurrentConfig(); // /susmodder-config
    
    foreach (var mod in config)
    {
        // Sprawdź czy jest nowsza wersja w historii
        var history = await GetModVersionHistory(mod.Id);
        if (history.Any())
        {
            var latest = history.First();
            if (latest.ModVersion != mod.ModVersion)
            {
                // Wyświetl notyfikację
                ShowUpdateNotification(mod.ModName, mod.ModVersion, latest.ModVersion);
            }
        }
    }
}
```

---

## ⚠️ Obsługa Błędów

```csharp
public async Task<ModVersionsResponse> GetModVersionsSafe(int? modId = null)
{
    try
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        var url = "https://api.susmodder.app/susmodder-config-versions";
        if (modId.HasValue)
            url += $"?modId={modId.Value}";
        
        var response = await httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"API Error: {response.StatusCode}");
            return null;
        }
        
        return await response.Content.ReadAsAsync<ModVersionsResponse>();
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Network Error: {ex.Message}");
        return null;
    }
    catch (TaskCanceledException ex)
    {
        Console.WriteLine("Request Timeout");
        return null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected Error: {ex.Message}");
        return null;
    }
}
```

---

## 📋 Checklist Integracji

- [ ] Dodaj model `ModVersionHistory` do projektu
- [ ] Dodaj model `ModVersionsResponse` do projektu
- [ ] Zaimplementuj metodę `GetModVersionHistory(int modId)`
- [ ] (Opcjonalnie) Dodaj UI do wyświetlania historii wersji
- [ ] (Opcjonalnie) Dodaj funkcję sprawdzania aktualizacji
- [ ] Przetestuj z prawdziwym API

---

## 🔗 Linki

- **Endpoint Dev:** `http://localhost:3001/susmodder-config-versions`
- **Endpoint Prod:** `https://api.susmodder.app/susmodder-config-versions`
- **Swagger:** `https://api.susmodder.app/api-docs`

---

## 💡 Wskazówki

1. **Cachowanie:** Rozważ cache'owanie historii wersji (zmienia się rzadko)
2. **Async/Await:** Zawsze używaj asynchronicznych metod HTTP
3. **Timeout:** Ustaw timeout na 10-15 sekund
4. **Retry:** Rozważ retry logic dla problemów sieciowych
5. **ModVersion jako string:** Wersje to stringi ("latest", "5.3.1 beta"), NIE liczby

---

**Ostatnia aktualizacja:** 2025-10-22  
**Wersja API:** 1.0  
**Kompatybilność:** Pełna wsteczna z istniejącym `/susmodder-config`
