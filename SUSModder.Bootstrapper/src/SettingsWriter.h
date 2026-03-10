#pragma once

#include <string>

namespace bootstrapper {

class SettingsWriter {
public:
    bool SaveSelectedChannelAndLanguage(const std::wstring& channel, const std::wstring& languageCode) const;
};

} // namespace bootstrapper
