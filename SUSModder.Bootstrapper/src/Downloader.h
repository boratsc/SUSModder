#pragma once

#include <functional>
#include <string>

namespace bootstrapper {

class Downloader {
public:
    using ProgressCallback = std::function<void(unsigned int)>;

    bool DownloadFile(const std::wstring& url, const std::wstring& destinationPath, const ProgressCallback& onProgress, std::wstring& errorMessage) const;
};

} // namespace bootstrapper
