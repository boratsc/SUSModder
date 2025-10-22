# Compatibility Matrix API - Code Examples

Gotowe do użycia przykłady integracji w różnych językach programowania.

---

## 📋 Spis Treści

- [JavaScript (Vanilla)](#javascript-vanilla)
- [JavaScript (Axios)](#javascript-axios)
- [TypeScript](#typescript)
- [React Hooks](#react-hooks)
- [Python](#python)
- [C# / .NET](#c--net)
- [PHP](#php)
- [Go](#go)
- [Java](#java)

---

## JavaScript (Vanilla)

### Basic Client

```javascript
// compatibility-api.js

const API_BASE = 'https://api.susmodder.app';

class CompatibilityAPI {
  /**
   * Pobierz kompatybilności dla moda DLL
   */
  async getDllCompatibilities(dllModId, options = {}) {
    const params = new URLSearchParams({
      dllModId: dllModId.toString()
    });

    if (options.status) {
      params.append('status', options.status); // np. 'F,W'
    }

    if (options.includeUntested !== undefined) {
      params.append('includeUntested', options.includeUntested.toString());
    }

    const response = await fetch(
      `${API_BASE}/api/compatibility?${params}`
    );

    if (!response.ok) {
      throw new Error(`API Error: ${response.status}`);
    }

    return response.json();
  }

  /**
   * Pobierz kompatybilności dla moda FULL
   */
  async getFullCompatibilities(fullModId, options = {}) {
    const params = new URLSearchParams({
      fullModId: fullModId.toString()
    });

    if (options.status) {
      params.append('status', options.status);
    }

    const response = await fetch(
      `${API_BASE}/api/compatibility?${params}`
    );

    if (!response.ok) {
      throw new Error(`API Error: ${response.status}`);
    }

    return response.json();
  }

  /**
   * Pobierz pełną macierz (wymaga tokenu)
   */
  async getMatrix(authToken) {
    const response = await fetch(
      `${API_BASE}/api/compatibility/matrix`,
      {
        headers: {
          'Authorization': authToken
        }
      }
    );

    if (!response.ok) {
      throw new Error(`API Error: ${response.status}`);
    }

    return response.json();
  }

  /**
   * Sprawdź czy konkretna para modów jest kompatybilna
   */
  async checkPairCompatibility(fullModId, dllModId) {
    const data = await this.getFullCompatibilities(fullModId);

    const compat = data.compatibilities.find(
      c => c.dllMod.id === dllModId
    );

    if (!compat) {
      return { compatible: null, status: 'UNKNOWN' };
    }

    return {
      compatible: ['F', 'W'].includes(compat.status),
      status: compat.status,
      recommended: compat.status === 'F',
      mod: compat.dllMod
    };
  }
}

// Użycie
const api = new CompatibilityAPI();

// Przykład 1: Pobierz kompatybilności dla DLL
api.getDllCompatibilities(5, { status: 'F,W' })
  .then(data => {
    console.log(`Znaleziono ${data.count} kompatybilnych modów`);
    data.compatibilities.forEach(comp => {
      console.log(`- ${comp.fullMod.name}: ${comp.status}`);
    });
  })
  .catch(error => {
    console.error('Błąd:', error);
  });

// Przykład 2: Sprawdź konkretną parę
api.checkPairCompatibility(1, 5)
  .then(result => {
    if (result.compatible) {
      console.log('✅ Mody są kompatybilne!');
    } else if (result.status === 'UNKNOWN') {
      console.log('⚠️  Brak danych o kompatybilności');
    } else {
      console.log('❌ Mody są niekompatybilne');
    }
  });
```

---

## JavaScript (Axios)

```javascript
// compatibility-api-axios.js

import axios from 'axios';

const API_BASE = 'https://api.susmodder.app';

export class CompatibilityAPI {
  constructor(authToken = null) {
    this.client = axios.create({
      baseURL: API_BASE,
      timeout: 10000,
      headers: authToken ? { 'Authorization': authToken } : {}
    });

    // Interceptor dla błędów
    this.client.interceptors.response.use(
      response => response,
      error => {
        if (error.response?.status === 404) {
          throw new Error('Mod nie znaleziony');
        } else if (error.response?.status === 401) {
          throw new Error('Brak autoryzacji');
        } else if (error.response?.status === 500) {
          throw new Error('Błąd serwera, spróbuj ponownie później');
        }
        throw error;
      }
    );
  }

  async getDllCompatibilities(dllModId, options = {}) {
    const { data } = await this.client.get('/api/compatibility', {
      params: {
        dllModId,
        ...options
      }
    });

    return data;
  }

  async getFullCompatibilities(fullModId, options = {}) {
    const { data } = await this.client.get('/api/compatibility', {
      params: {
        fullModId,
        ...options
      }
    });

    return data;
  }

  async getMatrix() {
    const { data } = await this.client.get('/api/compatibility/matrix');
    return data;
  }

  // Helper: Pobierz tylko polecane
  async getRecommended(modId, type = 'dll') {
    const method = type === 'dll'
      ? this.getDllCompatibilities
      : this.getFullCompatibilities;

    const data = await method.call(this, modId, { status: 'F' });

    return data.compatibilities;
  }
}

// Użycie
const api = new CompatibilityAPI();

try {
  const recommended = await api.getRecommended(5, 'dll');
  console.log('Polecane mody:', recommended);
} catch (error) {
  console.error('Błąd:', error.message);
}
```

---

## TypeScript

```typescript
// compatibility-api.ts

export interface Mod {
  id: number;
  name: string;
  version: string;
  currentVersion: string;
}

export interface Compatibility {
  id: number;
  status: 'F' | 'W' | 'NT' | 'NW';
  isCurrentVersion: boolean;
  warning?: string;
  fullMod?: Mod;
  dllMod?: Mod;
}

export interface CompatibilityResponse {
  success: boolean;
  query: {
    type: 'dll' | 'full';
    modId: number;
    modName: string;
    modVersion: string;
  };
  count: number;
  compatibilities: Compatibility[];
}

export interface MatrixResponse {
  success: boolean;
  fullMods: Array<{ id: number; name: string; version: string }>;
  dllMods: Array<{ id: number; name: string; version: string }>;
  matrix: Array<{
    fullModId: number;
    dllModId: number;
    status: 'F' | 'W' | 'NT' | 'NW';
    matrixId: number | null;
  }>;
}

export interface CompatibilityOptions {
  status?: string;
  includeUntested?: boolean;
  fullModVersion?: string;
  dllModVersion?: string;
}

export class CompatibilityAPIError extends Error {
  constructor(
    message: string,
    public statusCode?: number,
    public response?: any
  ) {
    super(message);
    this.name = 'CompatibilityAPIError';
  }
}

export class CompatibilityAPI {
  private baseURL: string;
  private authToken?: string;

  constructor(baseURL: string = 'https://api.susmodder.app', authToken?: string) {
    this.baseURL = baseURL;
    this.authToken = authToken;
  }

  private async request<T>(
    endpoint: string,
    params?: Record<string, string | number | boolean>
  ): Promise<T> {
    const url = new URL(endpoint, this.baseURL);

    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        url.searchParams.append(key, String(value));
      });
    }

    const headers: HeadersInit = {};
    if (this.authToken) {
      headers['Authorization'] = this.authToken;
    }

    const response = await fetch(url.toString(), { headers });

    if (!response.ok) {
      const error = await response.json().catch(() => ({}));
      throw new CompatibilityAPIError(
        error.error || `HTTP ${response.status}`,
        response.status,
        error
      );
    }

    return response.json();
  }

  async getDllCompatibilities(
    dllModId: number,
    options?: CompatibilityOptions
  ): Promise<CompatibilityResponse> {
    return this.request<CompatibilityResponse>('/api/compatibility', {
      dllModId,
      ...options
    });
  }

  async getFullCompatibilities(
    fullModId: number,
    options?: CompatibilityOptions
  ): Promise<CompatibilityResponse> {
    return this.request<CompatibilityResponse>('/api/compatibility', {
      fullModId,
      ...options
    });
  }

  async getMatrix(): Promise<MatrixResponse> {
    return this.request<MatrixResponse>('/api/compatibility/matrix');
  }

  async checkPairCompatibility(
    fullModId: number,
    dllModId: number
  ): Promise<{
    compatible: boolean | null;
    status: 'F' | 'W' | 'NT' | 'NW' | 'UNKNOWN';
    recommended: boolean;
  }> {
    const data = await this.getFullCompatibilities(fullModId);

    const compat = data.compatibilities.find(
      (c) => c.dllMod?.id === dllModId
    );

    if (!compat) {
      return { compatible: null, status: 'UNKNOWN', recommended: false };
    }

    return {
      compatible: ['F', 'W'].includes(compat.status),
      status: compat.status,
      recommended: compat.status === 'F'
    };
  }
}

// Użycie
const api = new CompatibilityAPI();

async function example() {
  try {
    const data = await api.getDllCompatibilities(5, { status: 'F,W' });

    data.compatibilities.forEach((comp) => {
      if (comp.fullMod) {
        console.log(`${comp.fullMod.name}: ${comp.status}`);
      }
    });

    const result = await api.checkPairCompatibility(1, 5);
    console.log('Kompatybilność:', result);
  } catch (error) {
    if (error instanceof CompatibilityAPIError) {
      console.error(`API Error (${error.statusCode}):`, error.message);
    } else {
      console.error('Błąd:', error);
    }
  }
}
```

---

## React Hooks

```typescript
// useCompatibility.ts

import { useState, useEffect } from 'react';
import { CompatibilityAPI, CompatibilityResponse } from './compatibility-api';

export function useCompatibility(
  modId: number,
  type: 'dll' | 'full',
  options?: { status?: string; includeUntested?: boolean }
) {
  const [data, setData] = useState<CompatibilityResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    const api = new CompatibilityAPI();
    const fetchData = async () => {
      try {
        setLoading(true);
        const result =
          type === 'dll'
            ? await api.getDllCompatibilities(modId, options)
            : await api.getFullCompatibilities(modId, options);

        setData(result);
        setError(null);
      } catch (err) {
        setError(err as Error);
      } finally {
        setLoading(false);
      }
    };

    fetchData();
  }, [modId, type, options?.status, options?.includeUntested]);

  return { data, loading, error };
}

// Komponent przykładowy
import React from 'react';

export function CompatibilityList({ dllModId }: { dllModId: number }) {
  const { data, loading, error } = useCompatibility(dllModId, 'dll', {
    status: 'F,W'
  });

  if (loading) return <div>Ładowanie...</div>;
  if (error) return <div>Błąd: {error.message}</div>;
  if (!data) return null;

  return (
    <div>
      <h3>Kompatybilne mody FULL ({data.count}):</h3>
      <ul>
        {data.compatibilities.map((comp) => (
          <li key={comp.id}>
            {comp.fullMod?.name} - <strong>{comp.status}</strong>
            {!comp.isCurrentVersion && (
              <span className="warning"> ⚠️ {comp.warning}</span>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
```

---

## Python

```python
# compatibility_api.py

import requests
from typing import Optional, List, Dict, Literal
from dataclasses import dataclass

@dataclass
class Mod:
    id: int
    name: str
    version: str
    current_version: str

@dataclass
class Compatibility:
    id: int
    status: Literal['F', 'W', 'NT', 'NW']
    is_current_version: bool
    full_mod: Optional[Mod] = None
    dll_mod: Optional[Mod] = None
    warning: Optional[str] = None

class CompatibilityAPI:
    def __init__(self, base_url: str = 'https://api.susmodder.app', auth_token: Optional[str] = None):
        self.base_url = base_url
        self.session = requests.Session()

        if auth_token:
            self.session.headers['Authorization'] = auth_token

    def get_dll_compatibilities(
        self,
        dll_mod_id: int,
        status: Optional[str] = None,
        include_untested: bool = True
    ) -> Dict:
        """Pobierz kompatybilności dla moda DLL"""
        params = {'dllModId': dll_mod_id}

        if status:
            params['status'] = status

        if not include_untested:
            params['includeUntested'] = 'false'

        response = self.session.get(
            f'{self.base_url}/api/compatibility',
            params=params
        )
        response.raise_for_status()

        return response.json()

    def get_full_compatibilities(
        self,
        full_mod_id: int,
        status: Optional[str] = None
    ) -> Dict:
        """Pobierz kompatybilności dla moda FULL"""
        params = {'fullModId': full_mod_id}

        if status:
            params['status'] = status

        response = self.session.get(
            f'{self.base_url}/api/compatibility',
            params=params
        )
        response.raise_for_status()

        return response.json()

    def get_matrix(self) -> Dict:
        """Pobierz pełną macierz kompatybilności"""
        response = self.session.get(
            f'{self.base_url}/api/compatibility/matrix'
        )
        response.raise_for_status()

        return response.json()

    def check_pair_compatibility(self, full_mod_id: int, dll_mod_id: int) -> Dict:
        """Sprawdź czy konkretna para modów jest kompatybilna"""
        data = self.get_full_compatibilities(full_mod_id)

        for compat in data['compatibilities']:
            if compat.get('dllMod', {}).get('id') == dll_mod_id:
                return {
                    'compatible': compat['status'] in ['F', 'W'],
                    'status': compat['status'],
                    'recommended': compat['status'] == 'F'
                }

        return {
            'compatible': None,
            'status': 'UNKNOWN',
            'recommended': False
        }

    def get_recommended_dlls(self, full_mod_id: int) -> List[Dict]:
        """Pobierz polecane dodatki DLL dla moda FULL"""
        data = self.get_full_compatibilities(full_mod_id, status='F')

        return [
            {
                'id': comp['dllMod']['id'],
                'name': comp['dllMod']['name'],
                'version': comp['dllMod']['version']
            }
            for comp in data['compatibilities']
        ]

# Użycie
if __name__ == '__main__':
    api = CompatibilityAPI()

    # Przykład 1: Pobierz kompatybilności
    try:
        data = api.get_dll_compatibilities(5, status='F,W')
        print(f"Znaleziono {data['count']} kompatybilnych modów")

        for comp in data['compatibilities']:
            full_mod = comp['fullMod']
            print(f"- {full_mod['name']}: {comp['status']}")

    except requests.exceptions.HTTPError as e:
        print(f"Błąd API: {e}")

    # Przykład 2: Sprawdź parę
    result = api.check_pair_compatibility(1, 5)
    if result['compatible']:
        print("✅ Mody są kompatybilne!")
        if result['recommended']:
            print("⭐ Polecana kombinacja!")
    else:
        print("❌ Mody mogą nie działać razem")
```

---

## C# / .NET

```csharp
// CompatibilityAPI.cs

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace SusModder.Compatibility
{
    public class Mod
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string CurrentVersion { get; set; }
    }

    public class Compatibility
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public bool IsCurrentVersion { get; set; }
        public Mod FullMod { get; set; }
        public Mod DllMod { get; set; }
        public string Warning { get; set; }
    }

    public class CompatibilityResponse
    {
        public bool Success { get; set; }
        public QueryInfo Query { get; set; }
        public int Count { get; set; }
        public List<Compatibility> Compatibilities { get; set; }
    }

    public class QueryInfo
    {
        public string Type { get; set; }
        public int ModId { get; set; }
        public string ModName { get; set; }
        public string ModVersion { get; set; }
    }

    public class CompatibilityAPI
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public CompatibilityAPI(string baseUrl = "https://api.susmodder.app", string authToken = null)
        {
            _baseUrl = baseUrl;
            _httpClient = new HttpClient();

            if (!string.IsNullOrEmpty(authToken))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", authToken);
            }
        }

        public async Task<CompatibilityResponse> GetDllCompatibilities(
            int dllModId,
            string status = null,
            bool includeUntested = true)
        {
            var queryParams = $"dllModId={dllModId}";

            if (!string.IsNullOrEmpty(status))
            {
                queryParams += $"&status={status}";
            }

            if (!includeUntested)
            {
                queryParams += "&includeUntested=false";
            }

            var url = $"{_baseUrl}/api/compatibility?{queryParams}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<CompatibilityResponse>(json, options);
        }

        public async Task<CompatibilityResponse> GetFullCompatibilities(
            int fullModId,
            string status = null)
        {
            var queryParams = $"fullModId={fullModId}";

            if (!string.IsNullOrEmpty(status))
            {
                queryParams += $"&status={status}";
            }

            var url = $"{_baseUrl}/api/compatibility?{queryParams}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<CompatibilityResponse>(json, options);
        }

        public async Task<(bool? Compatible, string Status, bool Recommended)> CheckPairCompatibility(
            int fullModId,
            int dllModId)
        {
            var data = await GetFullCompatibilities(fullModId);

            var compat = data.Compatibilities
                .FirstOrDefault(c => c.DllMod?.Id == dllModId);

            if (compat == null)
            {
                return (null, "UNKNOWN", false);
            }

            var compatible = compat.Status == "F" || compat.Status == "W";
            var recommended = compat.Status == "F";

            return (compatible, compat.Status, recommended);
        }
    }

    // Użycie
    class Program
    {
        static async Task Main(string[] args)
        {
            var api = new CompatibilityAPI();

            try
            {
                // Przykład 1
                var data = await api.GetDllCompatibilities(5, "F,W");
                Console.WriteLine($"Znaleziono {data.Count} kompatybilnych modów");

                foreach (var comp in data.Compatibilities)
                {
                    Console.WriteLine($"- {comp.FullMod.Name}: {comp.Status}");
                }

                // Przykład 2
                var (compatible, status, recommended) = await api.CheckPairCompatibility(1, 5);

                if (compatible == true)
                {
                    Console.WriteLine("✅ Mody są kompatybilne!");
                    if (recommended)
                    {
                        Console.WriteLine("⭐ Polecana kombinacja!");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Mody mogą nie działać razem");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Błąd API: {ex.Message}");
            }
        }
    }
}
```

---

## PHP

```php
<?php
// CompatibilityAPI.php

class CompatibilityAPI {
    private $baseUrl;
    private $authToken;

    public function __construct($baseUrl = 'https://api.susmodder.app', $authToken = null) {
        $this->baseUrl = rtrim($baseUrl, '/');
        $this->authToken = $authToken;
    }

    private function request($endpoint, $params = []) {
        $url = $this->baseUrl . $endpoint;

        if (!empty($params)) {
            $url .= '?' . http_build_query($params);
        }

        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'header' => $this->authToken
                    ? "Authorization: {$this->authToken}\r\n"
                    : '',
                'timeout' => 10
            ]
        ]);

        $response = file_get_contents($url, false, $context);

        if ($response === false) {
            throw new Exception('API request failed');
        }

        return json_decode($response, true);
    }

    public function getDllCompatibilities($dllModId, $status = null, $includeUntested = true) {
        $params = ['dllModId' => $dllModId];

        if ($status !== null) {
            $params['status'] = $status;
        }

        if (!$includeUntested) {
            $params['includeUntested'] = 'false';
        }

        return $this->request('/api/compatibility', $params);
    }

    public function getFullCompatibilities($fullModId, $status = null) {
        $params = ['fullModId' => $fullModId];

        if ($status !== null) {
            $params['status'] = $status;
        }

        return $this->request('/api/compatibility', $params);
    }

    public function getMatrix() {
        return $this->request('/api/compatibility/matrix');
    }

    public function checkPairCompatibility($fullModId, $dllModId) {
        $data = $this->getFullCompatibilities($fullModId);

        foreach ($data['compatibilities'] as $comp) {
            if (isset($comp['dllMod']['id']) && $comp['dllMod']['id'] === $dllModId) {
                return [
                    'compatible' => in_array($comp['status'], ['F', 'W']),
                    'status' => $comp['status'],
                    'recommended' => $comp['status'] === 'F'
                ];
            }
        }

        return [
            'compatible' => null,
            'status' => 'UNKNOWN',
            'recommended' => false
        ];
    }
}

// Użycie
$api = new CompatibilityAPI();

try {
    // Przykład 1
    $data = $api->getDllCompatibilities(5, 'F,W');
    echo "Znaleziono {$data['count']} kompatybilnych modów\n";

    foreach ($data['compatibilities'] as $comp) {
        echo "- {$comp['fullMod']['name']}: {$comp['status']}\n";
    }

    // Przykład 2
    $result = $api->checkPairCompatibility(1, 5);
    if ($result['compatible']) {
        echo "✅ Mody są kompatybilne!\n";
        if ($result['recommended']) {
            echo "⭐ Polecana kombinacja!\n";
        }
    } else {
        echo "❌ Mody mogą nie działać razem\n";
    }

} catch (Exception $e) {
    echo "Błąd: " . $e->getMessage() . "\n";
}
?>
```

---

## Go

```go
// compatibility_api.go

package main

import (
    "encoding/json"
    "fmt"
    "io"
    "net/http"
    "net/url"
    "time"
)

type Mod struct {
    ID             int    `json:"id"`
    Name           string `json:"name"`
    Version        string `json:"version"`
    CurrentVersion string `json:"currentVersion"`
}

type Compatibility struct {
    ID               int    `json:"id"`
    Status           string `json:"status"`
    IsCurrentVersion bool   `json:"isCurrentVersion"`
    FullMod          *Mod   `json:"fullMod,omitempty"`
    DllMod           *Mod   `json:"dllMod,omitempty"`
    Warning          string `json:"warning,omitempty"`
}

type CompatibilityResponse struct {
    Success         bool             `json:"success"`
    Query           QueryInfo        `json:"query"`
    Count           int              `json:"count"`
    Compatibilities []Compatibility  `json:"compatibilities"`
}

type QueryInfo struct {
    Type       string `json:"type"`
    ModID      int    `json:"modId"`
    ModName    string `json:"modName"`
    ModVersion string `json:"modVersion"`
}

type CompatibilityAPI struct {
    BaseURL    string
    AuthToken  string
    HTTPClient *http.Client
}

func NewCompatibilityAPI(baseURL, authToken string) *CompatibilityAPI {
    return &CompatibilityAPI{
        BaseURL:   baseURL,
        AuthToken: authToken,
        HTTPClient: &http.Client{
            Timeout: 10 * time.Second,
        },
    }
}

func (api *CompatibilityAPI) request(endpoint string, params map[string]string) ([]byte, error) {
    u, err := url.Parse(api.BaseURL + endpoint)
    if err != nil {
        return nil, err
    }

    q := u.Query()
    for key, value := range params {
        q.Set(key, value)
    }
    u.RawQuery = q.Encode()

    req, err := http.NewRequest("GET", u.String(), nil)
    if err != nil {
        return nil, err
    }

    if api.AuthToken != "" {
        req.Header.Set("Authorization", api.AuthToken)
    }

    resp, err := api.HTTPClient.Do(req)
    if err != nil {
        return nil, err
    }
    defer resp.Body.Close()

    if resp.StatusCode != http.StatusOK {
        return nil, fmt.Errorf("API error: %d", resp.StatusCode)
    }

    return io.ReadAll(resp.Body)
}

func (api *CompatibilityAPI) GetDllCompatibilities(dllModID int, status string) (*CompatibilityResponse, error) {
    params := map[string]string{
        "dllModId": fmt.Sprintf("%d", dllModID),
    }

    if status != "" {
        params["status"] = status
    }

    body, err := api.request("/api/compatibility", params)
    if err != nil {
        return nil, err
    }

    var result CompatibilityResponse
    if err := json.Unmarshal(body, &result); err != nil {
        return nil, err
    }

    return &result, nil
}

func (api *CompatibilityAPI) GetFullCompatibilities(fullModID int, status string) (*CompatibilityResponse, error) {
    params := map[string]string{
        "fullModId": fmt.Sprintf("%d", fullModID),
    }

    if status != "" {
        params["status"] = status
    }

    body, err := api.request("/api/compatibility", params)
    if err != nil {
        return nil, err
    }

    var result CompatibilityResponse
    if err := json.Unmarshal(body, &result); err != nil {
        return nil, err
    }

    return &result, nil
}

// Użycie
func main() {
    api := NewCompatibilityAPI("https://api.susmodder.app", "")

    // Przykład 1
    data, err := api.GetDllCompatibilities(5, "F,W")
    if err != nil {
        fmt.Printf("Błąd: %v\n", err)
        return
    }

    fmt.Printf("Znaleziono %d kompatybilnych modów\n", data.Count)
    for _, comp := range data.Compatibilities {
        if comp.FullMod != nil {
            fmt.Printf("- %s: %s\n", comp.FullMod.Name, comp.Status)
        }
    }
}
```

---

## Java

```java
// CompatibilityAPI.java

import com.fasterxml.jackson.databind.ObjectMapper;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.List;
import java.util.Optional;

public class CompatibilityAPI {
    private final String baseUrl;
    private final String authToken;
    private final HttpClient httpClient;
    private final ObjectMapper objectMapper;

    public CompatibilityAPI(String baseUrl, String authToken) {
        this.baseUrl = baseUrl;
        this.authToken = authToken;
        this.httpClient = HttpClient.newHttpClient();
        this.objectMapper = new ObjectMapper();
    }

    public CompatibilityResponse getDllCompatibilities(int dllModId, String status) throws Exception {
        String url = String.format("%s/api/compatibility?dllModId=%d", baseUrl, dllModId);

        if (status != null && !status.isEmpty()) {
            url += "&status=" + status;
        }

        HttpRequest.Builder requestBuilder = HttpRequest.newBuilder()
            .uri(URI.create(url))
            .GET();

        if (authToken != null && !authToken.isEmpty()) {
            requestBuilder.header("Authorization", authToken);
        }

        HttpRequest request = requestBuilder.build();
        HttpResponse<String> response = httpClient.send(request, HttpResponse.BodyHandlers.ofString());

        if (response.statusCode() != 200) {
            throw new Exception("API error: " + response.statusCode());
        }

        return objectMapper.readValue(response.body(), CompatibilityResponse.class);
    }

    public CompatibilityResponse getFullCompatibilities(int fullModId, String status) throws Exception {
        String url = String.format("%s/api/compatibility?fullModId=%d", baseUrl, fullModId);

        if (status != null && !status.isEmpty()) {
            url += "&status=" + status;
        }

        HttpRequest request = HttpRequest.newBuilder()
            .uri(URI.create(url))
            .GET()
            .build();

        HttpResponse<String> response = httpClient.send(request, HttpResponse.BodyHandlers.ofString());

        if (response.statusCode() != 200) {
            throw new Exception("API error: " + response.statusCode());
        }

        return objectMapper.readValue(response.body(), CompatibilityResponse.class);
    }

    // DTOs
    public static class Mod {
        public int id;
        public String name;
        public String version;
        public String currentVersion;
    }

    public static class Compatibility {
        public int id;
        public String status;
        public boolean isCurrentVersion;
        public Mod fullMod;
        public Mod dllMod;
        public String warning;
    }

    public static class CompatibilityResponse {
        public boolean success;
        public QueryInfo query;
        public int count;
        public List<Compatibility> compatibilities;
    }

    public static class QueryInfo {
        public String type;
        public int modId;
        public String modName;
        public String modVersion;
    }

    // Użycie
    public static void main(String[] args) {
        CompatibilityAPI api = new CompatibilityAPI("https://api.susmodder.app", null);

        try {
            CompatibilityResponse data = api.getDllCompatibilities(5, "F,W");
            System.out.println("Znaleziono " + data.count + " kompatybilnych modów");

            for (Compatibility comp : data.compatibilities) {
                if (comp.fullMod != null) {
                    System.out.println("- " + comp.fullMod.name + ": " + comp.status);
                }
            }
        } catch (Exception e) {
            System.err.println("Błąd: " + e.getMessage());
        }
    }
}
```

---

## 📝 Notatki

- Wszystkie przykłady są gotowe do użycia
- Dostosuj `baseUrl` do swojego środowiska (dev/prod)
- Implementuj retry logic dla produkcji
- Cachuj odpowiedzi API (5-10 minut)
- Obsługuj błędy HTTP (404, 500)

---

**Wersja dokumentu:** 1.0.0
**Data:** 2025-10-22
