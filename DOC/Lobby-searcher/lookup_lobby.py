#!/usr/bin/env python3
r"""Minimalny PoC lookupu lobby na modded regionach Among Us.

Flow:
1. code -> gameId
2. POST /api/user z globalnym idToken
3. GET /api/games/{gameId} z tokenem regionu

Przyklad:
  python DOC/Lobby-searcher/lookup_lobby.py ^
    --base-url https://au-eu.duikbo.at ^
    --id-token "<GLOBAL_ID_TOKEN>" ^
    --puid 000200ac488e47b4a8de6ea0e284b0f0 ^
    --username Apexspicy ^
    --client-version 50652900 ^
    --mods "1;2;auavengers.tou.mira=1.5.9;mira.api=0.3.9" ^
    PRIMAL
"""

from __future__ import annotations

import argparse
import json
import ssl
import sys
import time
import urllib.parse
import urllib.error
import urllib.request

V2 = "QWXRTYLPESDFGHUJKZOCVBINMA"
V2_MAP = {char: index for index, char in enumerate(V2)}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Lookup modded Among Us lobby by code")
    parser.add_argument("codes", nargs="*", help="Kod(y) lobby, np. PRIMAL ACTING")
    parser.add_argument("--base-url", required=True, help="Bazowy URL regionu, np. https://au-eu.duikbo.at")
    parser.add_argument("--id-token", required=True, help="Globalny Bearer token klienta / Innersloth/EOS")
    parser.add_argument("--puid", required=True, help="PUID gracza")
    parser.add_argument("--username", required=True, help="Nazwa gracza")
    parser.add_argument("--client-version", required=True, type=int, help="ClientVersion z requestu gry")
    parser.add_argument("--language", type=int, default=0, help="Language z requestu gry")
    parser.add_argument("--mods", default="", help="Naglowek Client-Mods")
    parser.add_argument("--mode", choices=("lookup", "list"), default="lookup", help="Tryb: lookup po kodzie lub listowanie lobby")
    parser.add_argument("--game-mode", type=int, default=1, help="GameMode do filtrowania listy lobby")
    parser.add_argument("--chat", type=int, default=1, help="AcceptedValues dla filtra chat")
    parser.add_argument("--list-language", type=int, default=256, help="AcceptedValues dla filtra language w listowaniu")
    parser.add_argument("--delay", type=float, default=0.0, help="Opoznienie miedzy lookupami w sekundach")
    parser.add_argument("--timeout", type=float, default=15.0)
    return parser.parse_args()


def game_name_to_int(code: str) -> int:
    code = code.strip().upper()
    if len(code) != 6 or any(char not in V2_MAP for char in code):
        raise ValueError(f"Nieprawidlowy 6-literowy kod: {code}")

    a, b, c, d, e, f = [V2_MAP[char] for char in code]
    one = (a + (26 * b)) & 0x3FF
    two = c + (26 * (d + (26 * (e + (26 * f)))))
    value = one | ((two << 10) & 0x3FFFFC00) | 0x80000000
    if value >= 2**31:
        value -= 2**32
    return value


def int_to_game_name(game_id: int) -> str:
    value = game_id & 0xFFFFFFFF
    a = value & 0x3FF
    b = (value >> 10) & 0xFFFFF
    return "".join(
        [
            V2[a % 26],
            V2[a // 26],
            V2[b % 26],
            V2[(b // 26) % 26],
            V2[(b // (26 * 26)) % 26],
            V2[(b // (26 * 26 * 26)) % 26],
        ]
    )


def request_text(url: str, method: str, headers: dict[str, str], body: bytes | None, timeout: float) -> str:
    request = urllib.request.Request(url=url, method=method, headers=headers, data=body)
    context = ssl.create_default_context()
    try:
        with urllib.request.urlopen(request, timeout=timeout, context=context) as response:
            return response.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        payload = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"HTTP {error.code} {error.reason}: {payload}") from error


def get_region_token(args: argparse.Namespace) -> str:
    payload = {
        "Puid": args.puid,
        "Username": args.username,
        "ClientVersion": args.client_version,
        "Language": args.language,
    }
    headers = {
        "Authorization": f"Bearer {args.id_token}",
        "Content-Type": "application/json",
        "Accept": "text/plain, */*",
        "User-Agent": "UnityPlayer/2022.3.44f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)",
        "X-Unity-Version": "2022.3.44f1",
    }
    body = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    return request_text(args.base_url.rstrip("/") + "/api/user", "POST", headers, body, args.timeout).strip()


def lookup_game(args: argparse.Namespace, region_token: str, game_id: int) -> dict:
    headers = {
        "Authorization": f"Bearer {region_token}",
        "Accept": "application/json",
        "User-Agent": "UnityPlayer/2022.3.44f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)",
        "X-Unity-Version": "2022.3.44f1",
    }
    if args.mods:
        headers["Client-Mods"] = args.mods
    url = args.base_url.rstrip("/") + f"/api/games/{game_id}"
    return json.loads(request_text(url, "GET", headers, None, args.timeout))


def build_filter_query(args: argparse.Namespace) -> str:
    payload = {
        "FilterSets": [
            {
                "GameMode": args.game_mode,
                "Filters": [
                    {
                        "OptionType": "chat",
                        "Key": "Chat",
                        "SubFilterString": json.dumps(
                            {"AcceptedValues": args.chat, "FilterType": "chat"},
                            separators=(",", ":"),
                        ),
                    },
                    {
                        "OptionType": "languages",
                        "Key": "Language",
                        "SubFilterString": json.dumps(
                            {"AcceptedValues": args.list_language, "FilterType": "languages"},
                            separators=(",", ":"),
                        ),
                    },
                ],
            }
        ]
    }
    return urllib.parse.quote(json.dumps(payload, separators=(",", ":")), safe="")


def list_games(args: argparse.Namespace, region_token: str) -> dict:
    headers = {
        "Authorization": f"Bearer {region_token}",
        "Accept": "application/json",
        "User-Agent": "UnityPlayer/2022.3.44f1 (UnityWebRequest/1.0, libcurl/8.5.0-DEV)",
        "X-Unity-Version": "2022.3.44f1",
    }
    if args.mods:
        headers["Client-Mods"] = args.mods
    query = build_filter_query(args)
    url = args.base_url.rstrip("/") + f"/api/games/filtered?filter={query}"
    return json.loads(request_text(url, "GET", headers, None, args.timeout))


def ip_to_string(ip_value: int | None) -> str:
    if ip_value is None:
        return ""
    octets = [(ip_value >> shift) & 0xFF for shift in (0, 8, 16, 24)]
    return ".".join(str(octet) for octet in octets)


def main() -> int:
    args = parse_args()
    try:
        region_token = get_region_token(args)
        print(f"Region token length: {len(region_token)}")

        if args.mode == "list":
            data = list_games(args, region_token)
            metadata = data.get("metadata") or {}
            print(
                "Matching={matching} All={all_games}".format(
                    matching=metadata.get("matchingGamesCount", "?"),
                    all_games=metadata.get("allGamesCount", "?"),
                )
            )
            for game in data.get("games", []):
                print(
                    "- {host} code={code} gameId={game_id} players={players}/{max_players} map={map_id} port={port} ip={ip} age={age}".format(
                        host=game.get("TrueHostName") or game.get("HostName") or "",
                        code=int_to_game_name(int(game.get("GameId", -1))) if game.get("GameId") is not None else "",
                        game_id=game.get("GameId", "?"),
                        players=game.get("PlayerCount", "?"),
                        max_players=game.get("MaxPlayers", "?"),
                        map_id=game.get("MapId", "?"),
                        port=game.get("Port", "?"),
                        ip=ip_to_string(game.get("IP")),
                        age=game.get("Age", "?"),
                    )
                )
            print(json.dumps(data, indent=2, ensure_ascii=True))
            return 0

        if not args.codes:
            raise ValueError("W trybie lookup podaj co najmniej jeden kod lobby")

        for raw_code in args.codes:
            code = raw_code.upper()
            game_id = game_name_to_int(code)
            print(f"\n=== {code} ===")
            print(f"GameId: {game_id}")
            try:
                data = lookup_game(args, region_token, game_id)
                game = data.get("Game") or {}
                print(
                    "Host={host} Players={players}/{max_players} Map={map_id} Port={port} Ip={ip} Age={age}".format(
                        host=game.get("TrueHostName") or game.get("HostName") or "",
                        players=game.get("PlayerCount", "?"),
                        max_players=game.get("MaxPlayers", "?"),
                        map_id=game.get("MapId", "?"),
                        port=game.get("Port", "?"),
                        ip=ip_to_string(game.get("IP")),
                        age=game.get("Age", "?"),
                    )
                )
                print(json.dumps(data, indent=2, ensure_ascii=True))
            except Exception as error:
                print(f"Lookup failed: {error}")

            if args.delay > 0:
                time.sleep(args.delay)
        return 0
    except Exception as error:
        print(f"Blad: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
