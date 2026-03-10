#pragma once

#include <string>

namespace bootstrapper {

class HashVerifier {
public:
    bool VerifyFileSha256(const std::wstring& filePath, const std::wstring& expectedHash, std::wstring& actualHash) const;
};

} // namespace bootstrapper
