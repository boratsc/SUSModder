#include "ManifestClient.h"

#include "Win32Helpers.h"

#include <windows.h>

#include <winhttp.h>

#include <algorithm>
#include <regex>
#include <vector>

namespace bootstrapper {

namespace {

std::wstring DownloadText(const std::wstring& url, std::wstring& errorMessage) {
    URL_COMPONENTS components{};
    std::vector<wchar_t> hostBuffer(256, 0);
    std::vector<wchar_t> pathBuffer(2048, 0);
    components.dwStructSize = sizeof(components);
    components.lpszHostName = hostBuffer.data();
    components.dwHostNameLength = static_cast<DWORD>(hostBuffer.size());
    components.lpszUrlPath = pathBuffer.data();
    components.dwUrlPathLength = static_cast<DWORD>(pathBuffer.size());
    components.dwSchemeLength = static_cast<DWORD>(-1);

    if (!WinHttpCrackUrl(url.c_str(), 0, 0, &components)) {
        errorMessage = L"Nie udalo sie sparsowac adresu URL manifestu.";
        return L"";
    }

    const std::wstring host(components.lpszHostName, components.dwHostNameLength);
    std::wstring path(components.lpszUrlPath, components.dwUrlPathLength);
    if (path.empty()) {
        path = L"/";
    }

    HINTERNET session = WinHttpOpen(L"SUSModderBootstrapper/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
        WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (session == nullptr) {
        errorMessage = L"Nie udalo sie otworzyc sesji HTTP dla manifestu.";
        return L"";
    }

    HINTERNET connection = WinHttpConnect(session, host.c_str(), components.nPort, 0);
    if (connection == nullptr) {
        WinHttpCloseHandle(session);
        errorMessage = L"Nie udalo sie polaczyc z serwerem manifestu.";
        return L"";
    }

    const DWORD requestFlags = components.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET request = WinHttpOpenRequest(connection, L"GET", path.c_str(), nullptr,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, requestFlags);
    if (request == nullptr) {
        WinHttpCloseHandle(connection);
        WinHttpCloseHandle(session);
        errorMessage = L"Nie udalo sie utworzyc zadania manifestu.";
        return L"";
    }

    std::string body;
    if (WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) &&
        WinHttpReceiveResponse(request, nullptr)) {
        DWORD statusCode = 0;
        DWORD statusCodeSize = sizeof(statusCode);
        WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX,
            &statusCode, &statusCodeSize, WINHTTP_NO_HEADER_INDEX);

        if (statusCode >= 200 && statusCode < 300) {
            std::vector<char> buffer(16 * 1024);
            while (true) {
                DWORD availableBytes = 0;
                if (!WinHttpQueryDataAvailable(request, &availableBytes)) {
                    errorMessage = L"Nie udalo sie sprawdzic danych manifestu.";
                    body.clear();
                    break;
                }

                if (availableBytes == 0) {
                    break;
                }

                DWORD bytesRead = 0;
                const DWORD chunkSize = std::min<DWORD>(availableBytes, static_cast<DWORD>(buffer.size()));
                if (!WinHttpReadData(request, buffer.data(), chunkSize, &bytesRead)) {
                    errorMessage = L"Nie udalo sie odczytac danych manifestu.";
                    body.clear();
                    break;
                }

                body.append(buffer.data(), bytesRead);
            }
        } else {
            errorMessage = L"Serwer zwrocil nieoczekiwany kod HTTP dla manifestu.";
        }
    } else {
        errorMessage = L"Nie udalo sie pobrac manifestu release.";
    }

    WinHttpCloseHandle(request);
    WinHttpCloseHandle(connection);
    WinHttpCloseHandle(session);

    if (body.empty()) {
        return L"";
    }

    const int wideLength = MultiByteToWideChar(CP_UTF8, 0, body.c_str(), static_cast<int>(body.size()), nullptr, 0);
    std::wstring wideBody(static_cast<size_t>(wideLength), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, body.c_str(), static_cast<int>(body.size()), &wideBody[0], wideLength);
    return wideBody;
}

bool ExtractStringValue(const std::wstring& json, const std::wstring& name, std::wstring& value) {
    const std::wstring pattern = L"\"" + name + L"\"\\s*:\\s*\"([^\"]+)\"";
    const std::wregex regex(pattern, std::regex_constants::icase);
    std::wsmatch match;
    if (!std::regex_search(json, match, regex) || match.size() < 2) {
        return false;
    }

    value = match[1].str();
    return true;
}

std::vector<ReleaseAsset> ExtractReleases(const std::wstring& json) {
    std::vector<ReleaseAsset> releases;
    const std::wregex releaseRegex(
        LR"_json(\{[^\{\}]*"Version"\s*:\s*"([^"]+)"[^\{\}]*"File"\s*:\s*"([^"]+)"[^\{\}]*"SHA256"\s*:\s*"([^"]+)"[^\{\}]*\})_json",
        std::regex_constants::icase);
    auto begin = std::wsregex_iterator(json.begin(), json.end(), releaseRegex);
    auto end = std::wsregex_iterator();
    for (auto it = begin; it != end; ++it) {
        ReleaseAsset asset;
        asset.version = (*it)[1].str();
        asset.fileName = (*it)[2].str();
        asset.sha256 = (*it)[3].str();
        asset.isFullPackage = ToLowerInvariant(asset.fileName).find(L"-full.nupkg") != std::wstring::npos;
        releases.push_back(asset);
    }

    return releases;
}

} // namespace

ReleaseManifest ManifestClient::LoadManifest(const std::wstring& channel, std::wstring& errorMessage) const {
    const std::wstring requestUrl = L"https://susmodder.app/api/releases?channel=" + channel;
    const std::wstring json = DownloadText(requestUrl, errorMessage);

    ReleaseManifest manifest;
    manifest.channel = channel;
    if (json.empty()) {
        manifest.success = false;
        return manifest;
    }

    manifest.success = json.find(L"\"success\": true") != std::wstring::npos || json.find(L"\"success\":true") != std::wstring::npos;
    ExtractStringValue(json, L"latestVersion", manifest.latestVersion);
    ExtractStringValue(json, L"downloadBaseUrl", manifest.downloadBaseUrl);
    ExtractStringValue(json, L"channel", manifest.channel);
    manifest.releases = ExtractReleases(json);

    if (!manifest.success || manifest.downloadBaseUrl.empty() || manifest.releases.empty()) {
        errorMessage = L"Manifest release nie zawieral wymaganych danych.";
        manifest.success = false;
    }

    return manifest;
}

} // namespace bootstrapper
