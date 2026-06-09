import json
import urllib.request
import urllib.parse
import urllib.error

BASE = "https://api.susmodder-cdn.ovh/v2"

cat = json.load(urllib.request.urlopen(f"{BASE}/catalog?limit=50"))
for m in cat["data"]:
    mid = m["id"]
    detail = json.load(urllib.request.urlopen(f"{BASE}/catalog/{mid}"))["data"]
    variants = detail.get("variants") or []
    ver = detail.get("currentVersion") or (variants[0].get("version") if variants else "")
    if not variants:
        print(f"NO_VARIANTS|{mid}|{m['name']}|{ver}")
        continue
    q = urllib.parse.urlencode({"platform": "steam", "arch": "x86"})
    url = f"{BASE}/downloads/mod/{mid}/{urllib.parse.quote(ver, safe='')}?{q}"
    req = urllib.request.Request(url, method="HEAD")
    try:
        r = urllib.request.urlopen(req)
        print(f"OK_DIRECT|{mid}|{m['name']}|{ver}|{r.status}")
    except urllib.error.HTTPError as e:
        if e.code in (301, 302, 303, 307, 308):
            loc = e.headers.get("Location", "")
            try:
                r2 = urllib.request.urlopen(urllib.request.Request(loc, method="HEAD"))
                print(f"REDIRECT_OK|{mid}|{m['name']}|{ver}|{r2.status}")
            except urllib.error.HTTPError as e2:
                print(f"REDIRECT_FAIL|{mid}|{m['name']}|{ver}|CDN_{e2.code}|{loc}")
            except Exception as ex:
                print(f"REDIRECT_ERR|{mid}|{m['name']}|{ver}|{ex}")
        else:
            print(f"API_FAIL|{mid}|{m['name']}|{ver}|{e.code}")
