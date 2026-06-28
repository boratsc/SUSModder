# Zależności Zewnętrzne: SUSModder na Linux

**Data:** 2025-10-30

---

## Spis treści

1. [Wymagania systemowe](#wymagania-systemowe)
2. [Zależności Runtime](#zależności-runtime)
3. [Instalacja per dystrybucja](#instalacja-per-dystrybucja)
4. [Opcjonalne zależności](#opcjonalne-zależności)
5. [Development dependencies](#development-dependencies)

---

## Wymagania systemowe

### Minimalne

| Komponent | Wymaganie |
|-----------|-----------|
| **System operacyjny** | Linux kernel 4.15+ (Ubuntu 18.04+, Fedora 28+, Arch Linux) |
| **Architektura** | x86_64 (64-bit) |
| **RAM** | 2 GB minimum, 4 GB recommended |
| **Dysk** | 500 MB dla aplikacji + 5 GB dla modów |
| **Display** | X11 lub Wayland |
| **Desktop Environment** | GNOME, KDE, XFCE, lub inne (opcjonalne) |

### Zalecane

| Komponent | Wymaganie |
|-----------|-----------|
| **System operacyjny** | Ubuntu 24.04 LTS, Fedora 40, Arch Linux (current) |
| **RAM** | 8 GB |
| **Dysk** | SSD z 10 GB wolnego miejsca |
| **Display** | 1920x1080 minimum |

---

## Zależności Runtime

### Obowiązkowe (Required)

#### 1. .NET Runtime 8.0

**Dlaczego:** SUSModder jest zbudowany na .NET 8.0

**Instalacja:**
```bash
# Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install dotnet-runtime-8.0

# Fedora
sudo dnf install dotnet-runtime-8.0

# Arch Linux
yay -S dotnet-runtime-8.0
```

**Weryfikacja:**
```bash
dotnet --version
# Powinno pokazać: 8.0.x
```

---

#### 2. p7zip (7-Zip)

**Dlaczego:** Ekstrakcja archiwów .7z (vanilla Among Us, mody)

**Instalacja:**
```bash
# Ubuntu/Debian
sudo apt install p7zip-full

# Fedora
sudo dnf install p7zip p7zip-plugins

# Arch Linux
sudo pacman -S p7zip
```

**Weryfikacja:**
```bash
7z --help
# Powinno pokazać help 7-Zip
```

---

#### 3. xdg-utils

**Dlaczego:** Otwieranie folderów, URL-i, desktop integration

**Instalacja:**
```bash
# Ubuntu/Debian (zazwyczaj preinstalowane)
sudo apt install xdg-utils

# Fedora (zazwyczaj preinstalowane)
sudo dnf install xdg-utils

# Arch Linux
sudo pacman -S xdg-utils
```

**Weryfikacja:**
```bash
xdg-open --version
```

---

#### 4. Steam

**Dlaczego:** Among Us jest grą Steam, Proton dla Windows compatibility

**Instalacja:**

**Ubuntu/Debian:**
```bash
# Metoda 1: Official repo
sudo apt install steam

# Metoda 2: Flatpak (zalecane dla nowszych dystrybucji)
flatpak install com.valvesoftware.Steam
```

**Fedora:**
```bash
# Enable RPM Fusion
sudo dnf install \
  https://download1.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm

# Install Steam
sudo dnf install steam
```

**Arch Linux:**
```bash
# Enable multilib w /etc/pacman.conf
sudo pacman -S steam
```

**Weryfikacja:**
```bash
steam --version
# lub
flatpak run com.valvesoftware.Steam
```

**Konfiguracja Proton:**
1. Uruchom Steam
2. Settings → Compatibility
3. Enable "Steam Play for all other titles"
4. Wybierz Proton Experimental lub najnowszy Proton GE

---

#### 5. pkexec (PolicyKit)

**Dlaczego:** Elevated permissions dla usuwania modów

**Instalacja:**
```bash
# Ubuntu/Debian (zazwyczaj preinstalowane w DE)
sudo apt install policykit-1

# Fedora
sudo dnf install polkit

# Arch Linux
sudo pacman -S polkit
```

**Weryfikacja:**
```bash
pkexec --version
```

---

### Opcjonalne dla funkcjonalności Epic Games

#### 6. Heroic Games Launcher

**Dlaczego:** Epic Games Store na Linux

**Instalacja:**

**Flatpak (zalecane):**
```bash
flatpak install flathub com.heroicgameslauncher.hgl
```

**AppImage:**
```bash
# Pobierz z https://heroicgameslauncher.com/
wget https://github.com/Heroic-Games-Launcher/HeroicGamesLauncher/releases/latest/download/heroic-*.AppImage
chmod +x heroic-*.AppImage
```

**Arch Linux (AUR):**
```bash
yay -S heroic-games-launcher-bin
```

**Weryfikacja:**
```bash
heroic --version
# lub
flatpak run com.heroicgameslauncher.hgl --version
```

---

## Instalacja per dystrybucja

### Ubuntu 24.04 LTS

```bash
#!/bin/bash
# install-dependencies-ubuntu.sh

# Aktualizuj system
sudo apt update && sudo apt upgrade -y

# Zainstaluj .NET Runtime
wget https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-runtime-8.0

# Zainstaluj p7zip
sudo apt install -y p7zip-full

# Zainstaluj xdg-utils (jeśli nie zainstalowane)
sudo apt install -y xdg-utils

# Zainstaluj PolicyKit
sudo apt install -y policykit-1

# Zainstaluj Steam
sudo apt install -y steam

# Opcjonalnie: Heroic Launcher (przez Flatpak)
sudo apt install -y flatpak
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install -y flathub com.heroicgameslauncher.hgl

echo "Wszystkie zależności zainstalowane!"
```

**Uruchom:**
```bash
chmod +x install-dependencies-ubuntu.sh
./install-dependencies-ubuntu.sh
```

---

### Fedora 40

```bash
#!/bin/bash
# install-dependencies-fedora.sh

# Aktualizuj system
sudo dnf update -y

# Zainstaluj .NET Runtime
sudo dnf install -y dotnet-runtime-8.0

# Zainstaluj p7zip
sudo dnf install -y p7zip p7zip-plugins

# Zainstaluj xdg-utils
sudo dnf install -y xdg-utils

# Zainstaluj PolicyKit
sudo dnf install -y polkit

# Enable RPM Fusion dla Steam
sudo dnf install -y \
  https://download1.rpmfusion.org/free/fedora/rpmfusion-free-release-$(rpm -E %fedora).noarch.rpm

# Zainstaluj Steam
sudo dnf install -y steam

# Opcjonalnie: Heroic Launcher
sudo dnf install -y flatpak
flatpak remote-add --if-not-exists flathub https://flathub.org/repo/flathub.flatpakrepo
flatpak install -y flathub com.heroicgameslauncher.hgl

echo "Wszystkie zależności zainstalowane!"
```

---

### Arch Linux

```bash
#!/bin/bash
# install-dependencies-arch.sh

# Aktualizuj system
sudo pacman -Syu --noconfirm

# Zainstaluj .NET Runtime (z AUR przez yay)
# Najpierw zainstaluj yay jeśli nie masz
if ! command -v yay &> /dev/null; then
    sudo pacman -S --noconfirm base-devel git
    git clone https://aur.archlinux.org/yay.git
    cd yay
    makepkg -si --noconfirm
    cd ..
    rm -rf yay
fi

yay -S --noconfirm dotnet-runtime-8.0

# Zainstaluj p7zip
sudo pacman -S --noconfirm p7zip

# Zainstaluj xdg-utils
sudo pacman -S --noconfirm xdg-utils

# Zainstaluj PolicyKit
sudo pacman -S --noconfirm polkit

# Zainstaluj Steam (enable multilib first)
# Edytuj /etc/pacman.conf i odkomentuj [multilib]
sudo sed -i "/\[multilib\]/,/Include/"'s/^#//' /etc/pacman.conf
sudo pacman -Sy --noconfirm
sudo pacman -S --noconfirm steam

# Opcjonalnie: Heroic Launcher
yay -S --noconfirm heroic-games-launcher-bin

echo "Wszystkie zależności zainstalowane!"
```

---

## Opcjonalne zależności

### 1. BepInEx for Proton

**Dlaczego:** Niektóre mody wymagają specjalnej wersji BepInEx dla Proton

**Instalacja:** Automatyczna przez SUSModder

**Manual install (jeśli potrzebne):**
```bash
cd ~/.steam/steam/steamapps/common/Among\ Us/
wget https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.1/BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip
unzip BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip
```

---

### 2. Proton GE (GloriousEggroll)

**Dlaczego:** Lepsza kompatybilność z niektórymi modami

**Instalacja:**
```bash
# ProtonUp-Qt (GUI tool)
flatpak install net.davidotek.pupgui2

# Lub manual:
cd ~/.steam/steam/compatibilitytools.d
wget https://github.com/GloriousEggroll/proton-ge-custom/releases/latest/download/GE-Proton*.tar.gz
tar -xf GE-Proton*.tar.gz
```

**Konfiguracja w Steam:**
1. Prawym → Among Us → Properties
2. Compatibility → Force the use of a specific Steam Play compatibility tool
3. Wybierz "GE-Proton..."

---

### 3. Gamescope (dla Steam Deck / lepszego performance)

**Instalacja:**
```bash
# Ubuntu/Debian
sudo apt install gamescope

# Fedora
sudo dnf install gamescope

# Arch
sudo pacman -S gamescope
```

---

## Development Dependencies

Dla developerów chcących budować SUSModder z source:

### Wymagane

```bash
# .NET SDK 8.0
# Ubuntu/Debian
sudo apt install dotnet-sdk-8.0

# Fedora
sudo dnf install dotnet-sdk-8.0

# Arch
yay -S dotnet-sdk-8.0

# Git
sudo apt install git  # Ubuntu/Debian
sudo dnf install git  # Fedora
sudo pacman -S git    # Arch

# Build essentials
sudo apt install build-essential  # Ubuntu/Debian
sudo dnf groupinstall "Development Tools"  # Fedora
sudo pacman -S base-devel  # Arch
```

### Opcjonalne (dla packaging)

```bash
# dpkg-deb (dla .deb packages)
sudo apt install dpkg-dev

# rpmbuild (dla .rpm packages)
sudo dnf install rpm-build

# AppImageTool
wget https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool-x86_64.AppImage
sudo mv appimagetool-x86_64.AppImage /usr/local/bin/appimagetool
```

---

## Podsumowanie zależności

### Tabela compatibility

| Dependency | Ubuntu 24.04 | Fedora 40 | Arch Linux | Obowiązkowe? |
|------------|--------------|-----------|------------|--------------|
| .NET Runtime 8.0 | ✅ | ✅ | ✅ | ✅ YES |
| p7zip | ✅ | ✅ | ✅ | ✅ YES |
| xdg-utils | ✅ (preinstalled) | ✅ (preinstalled) | ✅ | ✅ YES |
| Steam | ✅ | ✅ | ✅ | ✅ YES |
| pkexec | ✅ (preinstalled) | ✅ | ✅ | ✅ YES |
| Heroic Launcher | ⚠️ Optional | ⚠️ Optional | ⚠️ Optional | ❌ NO |
| Proton GE | ⚠️ Optional | ⚠️ Optional | ⚠️ Optional | ❌ NO |
| Gamescope | ⚠️ Optional | ⚠️ Optional | ⚠️ Optional | ❌ NO |

---

## Troubleshooting

### Problem: "7z command not found"

**Rozwiązanie:**
```bash
sudo apt install p7zip-full  # Ubuntu/Debian
sudo dnf install p7zip       # Fedora
sudo pacman -S p7zip          # Arch
```

---

### Problem: "dotnet: command not found"

**Rozwiązanie:**
```bash
# Sprawdź czy zainstalowany
which dotnet

# Jeśli nie, zainstaluj .NET Runtime 8.0
# (zobacz sekcję instalacji powyżej)
```

---

### Problem: "xdg-open: no method available for opening..."

**Rozwiązanie:**
```bash
# Zainstaluj xdg-utils
sudo apt install xdg-utils

# Ustaw default applications
xdg-settings set default-web-browser firefox.desktop
xdg-mime default org.gnome.Nautilus.desktop inode/directory
```

---

### Problem: Steam nie wykrywa Among Us

**Rozwiązanie:**
1. Sprawdź czy gra zainstalowana: `ls ~/.steam/steam/steamapps/common/Among\ Us`
2. Włącz Proton w Steam Settings → Compatibility
3. Zrestartuj Steam

---

### Problem: pkexec authorization failed

**Rozwiązanie:**
```bash
# Sprawdź czy polkit działa
systemctl status polkit

# Jeśli nie, uruchom
sudo systemctl start polkit

# Alternatywnie, użyj sudo (wymaga terminal)
```

---

**Następny dokument:** [06-RISKS-AND-CHALLENGES.md](./06-RISKS-AND-CHALLENGES.md) - Ryzyka i wyzwania techniczne
