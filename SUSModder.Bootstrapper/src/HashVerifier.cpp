#include "HashVerifier.h"

#include "Win32Helpers.h"

#include <windows.h>

#include <bcrypt.h>

#include <iomanip>
#include <sstream>
#include <vector>

namespace bootstrapper {

namespace {

std::wstring BytesToHex(const std::vector<unsigned char>& bytes) {
    std::wstringstream stream;
    stream << std::hex << std::setfill(L'0');
    for (unsigned char byte : bytes) {
        stream << std::setw(2) << static_cast<int>(byte);
    }

    return ToLowerInvariant(stream.str());
}

} // namespace

bool HashVerifier::VerifyFileSha256(const std::wstring& filePath, const std::wstring& expectedHash, std::wstring& actualHash) const {
    actualHash.clear();

    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    HANDLE fileHandle = INVALID_HANDLE_VALUE;
    std::vector<unsigned char> objectBuffer;
    std::vector<unsigned char> hashBuffer;
    std::vector<unsigned char> readBuffer(64 * 1024);

    DWORD objectLength = 0;
    DWORD hashLength = 0;
    DWORD bytesReturned = 0;

    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0) {
        return false;
    }

    if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &bytesReturned, 0) != 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return false;
    }

    if (BCryptGetProperty(algorithm, BCRYPT_HASH_LENGTH, reinterpret_cast<PUCHAR>(&hashLength), sizeof(hashLength), &bytesReturned, 0) != 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return false;
    }

    objectBuffer.resize(objectLength);
    hashBuffer.resize(hashLength);

    if (BCryptCreateHash(algorithm, &hash, objectBuffer.data(), objectLength, nullptr, 0, 0) != 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return false;
    }

    fileHandle = CreateFileW(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (fileHandle == INVALID_HANDLE_VALUE) {
        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return false;
    }

    DWORD bytesRead = 0;
    while (ReadFile(fileHandle, readBuffer.data(), static_cast<DWORD>(readBuffer.size()), &bytesRead, nullptr) && bytesRead > 0) {
        if (BCryptHashData(hash, readBuffer.data(), bytesRead, 0) != 0) {
            CloseHandle(fileHandle);
            BCryptDestroyHash(hash);
            BCryptCloseAlgorithmProvider(algorithm, 0);
            return false;
        }
    }

    const bool finalizeOk = BCryptFinishHash(hash, hashBuffer.data(), hashLength, 0) == 0;
    CloseHandle(fileHandle);
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);

    if (!finalizeOk) {
        return false;
    }

    actualHash = BytesToHex(hashBuffer);
    return actualHash == ToLowerInvariant(Trim(expectedHash));
}

} // namespace bootstrapper
