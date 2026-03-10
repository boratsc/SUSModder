#pragma once

#include <string>
#include <vector>

namespace bootstrapper {

struct ReleaseAsset {
    std::wstring version;
    std::wstring fileName;
    std::wstring sha256;
    bool isFullPackage = true;
};

struct ReleaseManifest {
    bool success = false;
    std::wstring channel;
    std::wstring latestVersion;
    std::wstring downloadBaseUrl;
    std::vector<ReleaseAsset> releases;
};

} // namespace bootstrapper
