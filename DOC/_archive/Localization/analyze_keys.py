#!/usr/bin/env python3
"""
Analiza kluczy lokalizacyjnych w SUSModder.
Sprawdza:
1. Które klucze są zdefiniowane w pl.json i en.json
2. Które klucze są używane w kodzie (AXAML + C#)
3. Które klucze są nadmiarowe (zdefiniowane ale nie używane)
4. Które klucze są brakujące (używane ale nie zdefiniowane)
"""

import json
import re
import os
from pathlib import Path
from typing import Set, Dict, List

# Ścieżki
ROOT_DIR = Path(__file__).parent
PL_JSON = ROOT_DIR / "SUSModder" / "Localization" / "pl.json"
EN_JSON = ROOT_DIR / "SUSModder" / "Localization" / "en.json"
VIEWS_DIR = ROOT_DIR / "SUSModder" / "Views"
VIEWMODELS_DIR = ROOT_DIR / "SUSModder" / "ViewModels"


def flatten_json(data: dict, prefix: str = "") -> Set[str]:
    """Spłaszcza zagnieżdżony JSON do listy kluczy (np. UI.Buttons.Install)"""
    keys = set()
    for key, value in data.items():
        full_key = f"{prefix}.{key}" if prefix else key
        if isinstance(value, dict):
            keys.update(flatten_json(value, full_key))
        else:
            keys.add(full_key)
    return keys


def extract_keys_from_axaml(file_path: Path) -> Set[str]:
    """Wyciąga klucze lokalizacyjne z pliku AXAML: {local:Localize Key}"""
    keys = set()
    try:
        content = file_path.read_text(encoding='utf-8')
        # Regex dla {local:Localize Key}
        pattern = r'\{local:Localize\s+([A-Za-z.]+)\}'
        matches = re.findall(pattern, content)
        keys.update(matches)
    except Exception as e:
        print(f"⚠️ Błąd odczytu {file_path.name}: {e}")
    return keys


def extract_keys_from_cs(file_path: Path) -> Set[str]:
    """Wyciąga klucze lokalizacyjne z pliku C#: Get("Key") lub GetFormatted("Key")"""
    keys = set()
    try:
        content = file_path.read_text(encoding='utf-8')
        # Regex dla _localizationService.Get("Key")
        pattern1 = r'_localizationService\.Get\("([^"]+)"\)'
        # Regex dla _localizationService.GetFormatted("Key", ...)
        pattern2 = r'_localizationService\.GetFormatted\("([^"]+)"'
        
        matches1 = re.findall(pattern1, content)
        matches2 = re.findall(pattern2, content)
        keys.update(matches1)
        keys.update(matches2)
    except Exception as e:
        print(f"⚠️ Błąd odczytu {file_path.name}: {e}")
    return keys


def scan_directory(directory: Path, file_ext: str, extractor_func) -> Set[str]:
    """Skanuje katalog w poszukiwaniu plików z określonym rozszerzeniem"""
    keys = set()
    if not directory.exists():
        print(f"⚠️ Katalog nie istnieje: {directory}")
        return keys
    
    for file_path in directory.rglob(f"*{file_ext}"):
        file_keys = extractor_func(file_path)
        keys.update(file_keys)
    
    return keys


def compare_json_files(pl_keys: Set[str], en_keys: Set[str]) -> Dict[str, List[str]]:
    """Porównuje klucze w pl.json i en.json"""
    only_pl = pl_keys - en_keys
    only_en = en_keys - pl_keys
    return {
        "only_pl": sorted(only_pl),
        "only_en": sorted(only_en)
    }


def main():
    print("=" * 80)
    print("📊 ANALIZA KLUCZY LOKALIZACYJNYCH - SUSModder")
    print("=" * 80)
    print()
    
    # 1. Wczytaj klucze z JSON
    print("1️⃣ Wczytywanie kluczy z plików JSON...")
    with open(PL_JSON, 'r', encoding='utf-8') as f:
        pl_data = json.load(f)
    with open(EN_JSON, 'r', encoding='utf-8') as f:
        en_data = json.load(f)
    
    pl_keys = flatten_json(pl_data)
    en_keys = flatten_json(en_data)
    
    print(f"   ✅ pl.json: {len(pl_keys)} kluczy")
    print(f"   ✅ en.json: {len(en_keys)} kluczy")
    print()
    
    # 2. Porównaj pl.json i en.json
    print("2️⃣ Porównanie pl.json vs en.json...")
    diff = compare_json_files(pl_keys, en_keys)
    if diff["only_pl"]:
        print(f"   ⚠️ Klucze tylko w pl.json ({len(diff['only_pl'])}):")
        for key in diff["only_pl"][:10]:  # Pokaż max 10
            print(f"      - {key}")
        if len(diff["only_pl"]) > 10:
            print(f"      ... i {len(diff['only_pl']) - 10} więcej")
    else:
        print("   ✅ Brak kluczy tylko w pl.json")
    
    if diff["only_en"]:
        print(f"   ⚠️ Klucze tylko w en.json ({len(diff['only_en'])}):")
        for key in diff["only_en"][:10]:
            print(f"      - {key}")
        if len(diff["only_en"]) > 10:
            print(f"      ... i {len(diff['only_en']) - 10} więcej")
    else:
        print("   ✅ Brak kluczy tylko w en.json")
    print()
    
    # 3. Skanuj użycie kluczy w kodzie
    print("3️⃣ Skanowanie użycia kluczy w kodzie...")
    axaml_keys = scan_directory(VIEWS_DIR, ".axaml", extract_keys_from_axaml)
    cs_keys = scan_directory(VIEWS_DIR, ".axaml.cs", extract_keys_from_cs)
    viewmodel_keys = scan_directory(VIEWMODELS_DIR, ".cs", extract_keys_from_cs)
    
    used_keys = axaml_keys | cs_keys | viewmodel_keys
    
    print(f"   ✅ Klucze w AXAML: {len(axaml_keys)}")
    print(f"   ✅ Klucze w code-behind: {len(cs_keys)}")
    print(f"   ✅ Klucze w ViewModels: {len(viewmodel_keys)}")
    print(f"   📊 Łącznie używanych kluczy: {len(used_keys)}")
    print()
    
    # 4. Znajdź nadmiarowe klucze (zdefiniowane ale nie używane)
    print("4️⃣ Analiza nadmiarowych kluczy...")
    unused_keys = pl_keys - used_keys
    
    if unused_keys:
        print(f"   ⚠️ Znaleziono {len(unused_keys)} potencjalnie nadmiarowych kluczy:")
        # Grupuj według sekcji
        grouped = {}
        for key in sorted(unused_keys):
            section = key.split('.')[0]
            if section not in grouped:
                grouped[section] = []
            grouped[section].append(key)
        
        for section, keys in sorted(grouped.items()):
            print(f"\n   📁 {section} ({len(keys)} kluczy):")
            for key in keys[:15]:  # Max 15 na sekcję
                print(f"      - {key}")
            if len(keys) > 15:
                print(f"      ... i {len(keys) - 15} więcej")
    else:
        print("   ✅ Wszystkie klucze są używane!")
    print()
    
    # 5. Znajdź brakujące klucze (używane ale nie zdefiniowane)
    print("5️⃣ Analiza brakujących kluczy...")
    missing_keys = used_keys - pl_keys
    
    if missing_keys:
        print(f"   ❌ Znaleziono {len(missing_keys)} brakujących kluczy:")
        for key in sorted(missing_keys):
            print(f"      - {key}")
    else:
        print("   ✅ Wszystkie używane klucze są zdefiniowane!")
    print()
    
    # 6. Podsumowanie
    print("=" * 80)
    print("📋 PODSUMOWANIE")
    print("=" * 80)
    print(f"Zdefiniowane klucze (pl.json):     {len(pl_keys)}")
    print(f"Zdefiniowane klucze (en.json):     {len(en_keys)}")
    print(f"Używane klucze (AXAML + C#):       {len(used_keys)}")
    print(f"Nadmiarowe klucze:                 {len(unused_keys)} ({len(unused_keys)/len(pl_keys)*100:.1f}%)")
    print(f"Brakujące klucze:                  {len(missing_keys)}")
    print()
    
    if len(unused_keys) > 0:
        print("💡 Rekomendacja: Usuń nadmiarowe klucze aby zredukować rozmiar plików tłumaczeń.")
    
    if len(missing_keys) > 0:
        print("⚠️ Uwaga: Dodaj brakujące klucze do pl.json i en.json!")
    
    if len(unused_keys) == 0 and len(missing_keys) == 0:
        print("✅ System lokalizacji jest w pełni zoptymalizowany!")
    
    print()
    print("=" * 80)


if __name__ == "__main__":
    main()
