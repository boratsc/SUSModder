# Build scripts — quick reference

## Aktualne użycie

Podstawowy flow release/beta jest bez podpisywania kodu. Certyfikat wygasł i nie jest odnawiany.

```powershell
# Release + beta
.\build-dual-channel.ps1 -Version 3.0.0

# Tylko release
.\build-dual-channel.ps1 -Version 3.0.0 -SkipBeta

# Tylko beta
.\build-dual-channel.ps1 -Version 3.1.0 -SkipRelease
```

Output:

- `releases-release/` — kanał release Velopack
- `releases-beta/` — kanał beta Velopack

## Skrypty

- `build-dual-channel.ps1` — rekomendowany build developerski/produkcyjny bez podpisywania.
- `build-release-velopack.ps1` — helper dla pojedynczego kanału Velopack.
- `build-velopack-test.ps1` — lokalne testy paczkowania Velopack.
- `build-bootstrapper.ps1` — build bootstrappera/instalatora, jeśli jest potrzebny w release flow.
- `deploy-to-server.ps1` — ręczny upload gotowych artefaktów; wymaga jawnych parametrów/klucza SSH i ostrożności.
- `build-release-2.2.0.ps1`, `sign-and-build.ps1`, `build-with-signing.ps1`, `post-sign-packages.ps1` — legacy/reference dla starego flow podpisywania albo ZIP. Nie używać domyślnie.

## Zasady

1. Nie commituj katalogów `publish*`, `releases-*`, `velopack-releases`, ani logów z buildów.
2. Nie zapisuj haseł, tokenów ani prywatnych kluczy w skryptach.
3. Jeśli stary skrypt nie jest już potrzebny jako referencja, przenieś go do archiwum albo usuń osobnym cleanupem.
