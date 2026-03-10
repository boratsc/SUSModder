#pragma once

#include <string>

namespace bootstrapper {

std::wstring GetAppDataRoot();
std::wstring GetAppLogDirectory();
std::wstring GetTempDownloadDirectory();
std::wstring GetUserSettingsDirectory();
std::wstring GetDefaultInstallRoot();
bool EnsureDirectoryExists(const std::wstring& path);
bool WriteTextFile(const std::wstring& path, const std::wstring& contents);
bool RunProcessAndWait(const std::wstring& commandLine, unsigned long& exitCode);
bool LaunchProcessDetached(const std::wstring& executablePath);
std::wstring GetLastErrorMessage(unsigned long errorCode);
bool RegisterApplicationInUninstallRegistry(const std::wstring& appVersion, const std::wstring& launchExePath, const std::wstring& installRoot);
bool CreateApplicationShortcuts(const std::wstring& launchExePath, bool createStartMenuShortcut, bool createDesktopShortcut, std::wstring& errorMessage);
std::wstring Trim(const std::wstring& value);
std::wstring ToLowerInvariant(const std::wstring& value);
std::wstring JoinUrl(const std::wstring& baseUrl, const std::wstring& relativePath);

} // namespace bootstrapper
