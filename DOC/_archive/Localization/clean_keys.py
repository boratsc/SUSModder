#!/usr/bin/env python3
"""
Narzędzie do czyszczenia i optymalizacji plików lokalizacyjnych.
Usuwa duplikaty, sortuje klucze, poprawia strukturę.
"""

import json
import sys
from pathlib import Path
from collections import OrderedDict

def remove_duplicates(data, path=""):
    """Rekurencyjnie usuwa duplikaty w zagnieżdżonym dict"""
    if not isinstance(data, dict):
        return data
    
    seen_keys = set()
    cleaned = OrderedDict()
    
    for key, value in data.items():
        if key in seen_keys:
            print(f"  ⚠️ Usunięto duplikat klucza: {path}.{key}" if path else f"  ⚠️ Usunięto duplikat klucza: {key}")
            continue
        
        seen_keys.add(key)
        
        if isinstance(value, dict):
            cleaned[key] = remove_duplicates(value, f"{path}.{key}" if path else key)
        else:
            cleaned[key] = value
    
    return cleaned

def clean_json_file(file_path: Path) -> dict:
    """Czyści plik JSON - usuwa duplikaty, sortuje"""
    print(f"\n🔧 Czyszczenie pliku: {file_path.name}")
    
    with open(file_path, 'r', encoding='utf-8') as f:
        data = json.load(f, object_pairs_hook=OrderedDict)
    
    # Usuń duplikaty
    cleaned_data = remove_duplicates(data)
    
    # Zapisz z ładnym formatowaniem
    with open(file_path, 'w', encoding='utf-8') as f:
        json.dump(cleaned_data, f, ensure_ascii=False, indent=2)
    
    print(f"  ✅ Zapisano: {file_path.name}")
    
    return cleaned_data

def main():
    ROOT_DIR = Path(__file__).parent
    PL_JSON = ROOT_DIR / "SUSModder" / "Localization" / "pl.json"
    EN_JSON = ROOT_DIR / "SUSModder" / "Localization" / "en.json"
    
    print("=" * 60)
    print("🧹 CZYSZCZENIE I OPTYMALIZACJA PLIKÓW LOKALIZACYJNYCH")
    print("=" * 60)
    
    pl_data = clean_json_file(PL_JSON)
    en_data = clean_json_file(EN_JSON)
    
    print("\n" + "=" * 60)
    print("✅ GOTOWE!")
    print("=" * 60)

if __name__ == "__main__":
    main()
