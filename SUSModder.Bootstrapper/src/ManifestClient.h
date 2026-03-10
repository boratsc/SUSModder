#pragma once

#include "ManifestModels.h"

#include <string>

namespace bootstrapper {

class ManifestClient {
public:
    ReleaseManifest LoadManifest(const std::wstring& channel, std::wstring& errorMessage) const;
};

} // namespace bootstrapper
