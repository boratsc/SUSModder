#!/usr/bin/env python3
r"""Minimalny probe HTTP matchmakingu dla modowanych regionow Among Us.

Skrypt nie probuje obchodzic auth. Jego celem jest:
- wczytanie regionow z regionInfo.json
- odpytanie typowych endpointow matchmakingu
- pokazanie, czy backend odpowiada oraz czy wymaga Authorization

Przyklady:
  python probe_matchmaker.py --region-file "C:\Users\Administrator\AppData\LocalLow\Innersloth\Among Us\regionInfo.json"
  python probe_matchmaker.py --base-url https://au-eu.duikbo.at --token "Bearer ..."
"""

from __future__ import annotations

import argparse
import json
import ssl
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


DEFAULT_REGION_FILE = Path(r"C:\Users\Administrator\AppData\LocalLow\Innersloth\Among Us\regionInfo.json")
DEFAULT_ENDPOINTS = (
    ("GET", "/api/games"),
    ("GET", "/api/games/filtered"),
    ("POST", "/api/user"),
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Probe modded Among Us matchmaking endpoints")
    parser.add_argument("--region-file", type=Path, default=DEFAULT_REGION_FILE)
    parser.add_argument("--base-url", action="append", default=[])
    parser.add_argument("--token", default="", help="Naglowek Authorization, np. 'Bearer ...'")
    parser.add_argument("--timeout", type=float, default=10.0)
    return parser.parse_args()


def load_region_urls(region_file: Path) -> list[str]:
    data = json.loads(region_file.read_text(encoding="utf-8"))
    urls: list[str] = []

    for region in data.get("Regions", []):
        for server in region.get("Servers", []):
            url = str(server.get("Ip", "")).strip()
            if url.startswith("https://") or url.startswith("http://"):
                urls.append(url.rstrip("/"))

    return list(dict.fromkeys(urls))


def make_request(method: str, url: str, token: str, timeout: float) -> tuple[int | None, str, str]:
    headers = {
        "User-Agent": "SUSModder-Lobby-Probe/0.1",
        "Accept": "application/json, text/plain, */*",
    }
    data = None

    if token:
        headers["Authorization"] = token

    if method == "POST":
        headers["Content-Type"] = "application/json"
        data = b"{}"

    request = urllib.request.Request(url=url, method=method, headers=headers, data=data)
    context = ssl.create_default_context()

    try:
        with urllib.request.urlopen(request, timeout=timeout, context=context) as response:
            body = response.read(400).decode("utf-8", errors="replace")
            return response.status, response.reason, body
    except urllib.error.HTTPError as error:
        body = error.read(400).decode("utf-8", errors="replace")
        return error.code, error.reason, body
    except Exception as error:  # pragma: no cover - probe diagnostyczny
        return None, type(error).__name__, str(error)


def main() -> int:
    args = parse_args()

    urls = [url.rstrip("/") for url in args.base_url]
    if not urls:
        if not args.region_file.exists():
            print(f"Brak pliku regionow: {args.region_file}", file=sys.stderr)
            return 1
        urls = load_region_urls(args.region_file)

    if not urls:
        print("Nie znaleziono zadnych adresow bazowych do testu.", file=sys.stderr)
        return 1

    print("== Matchmaker probe ==")
    print(f"Token ustawiony: {'tak' if bool(args.token) else 'nie'}")

    for base_url in urls:
        print(f"\n## {base_url}")
        for method, path in DEFAULT_ENDPOINTS:
            full_url = urllib.parse.urljoin(base_url + "/", path.lstrip("/"))
            status, reason, body = make_request(method, full_url, args.token, args.timeout)
            print(f"{method:4} {path:20} -> {status} {reason}")
            if body:
                one_line = " ".join(body.splitlines()).strip()
                print(f"      {one_line[:220]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
