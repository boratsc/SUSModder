#pragma once

#include "ManifestModels.h"

#include <functional>
#include <string>

namespace bootstrapper {

class Installer {
public:
    using StatusCallback = std::function<void(const std::wstring&)>;

    bool PrepareSeedInstall(const std::wstring& packagePath,
        const ReleaseAsset& asset,
        const std::wstring& channel,
        const std::wstring& updateExePath,
        const StatusCallback& onStatus,
        std::wstring& errorMessage,
        std::wstring& launchPath) const;
};

} // namespace bootstrapper
