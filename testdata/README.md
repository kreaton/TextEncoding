# Test data

Encoding samples used by `tests/TextEncoding.Core.Tests` (copied to the test output folder). All text files use
CRLF line endings. Regenerate with `pwsh testdata/generate.ps1`; don't let an editor or git re-encode them
(see `.gitattributes`).

| File | Encoding | BOM | Content |
|---|---|---|---|
| `ascii.txt` | 7-bit ASCII | – | ASCII sample |
| `utf8.txt` | UTF-8 | no | Unicode sample |
| `utf8-bom.txt` | UTF-8 | yes | Unicode sample |
| `utf16le-bom.txt` / `utf16be-bom.txt` | UTF-16 LE / BE | yes | Unicode sample |
| `utf16le-nobom.txt` / `utf16be-nobom.txt` | UTF-16 LE / BE | no | Unicode sample |
| `utf32le-bom.txt` / `utf32be-bom.txt` | UTF-32 LE / BE | yes | Unicode sample |
| `utf8-latin.txt` | UTF-8 | no | Latin sample |
| `windows-1252.txt` | Windows-1252 | – | Latin sample (not valid UTF-8) |
| `utf8-nordic.txt` | UTF-8 | no | Nordic sample |
| `iso-8859-1.txt` | ISO-8859-1 (Latin-1) | – | Nordic sample |
| `ibm850.txt` | DOS code page 850 | – | Nordic sample |
| `windows-1252-c1.txt` | Windows-1252 | – | bytes 0x80–0xA0, incl. the undefined 0x8F / 0x9D |
| `windows-1252-c1.utf8.txt` | UTF-8 | no | expected conversion of the file above |
| `utf8-truncated.txt` | broken UTF-8 | no | ends in the middle of a multi-byte sequence |
| `empty.txt` | – | – | 0 bytes |

* **Nordic sample**: one line each of Danish (ÆØÅ æøå), Swedish (ÅÄÖ åäö) and German (ÄÖÜ äöüß); fits
  ISO-8859-1, Windows-1252 and CP850.
* **Latin sample**: "Hello, world!", the Nordic sample, and a line with curly quotes, en dash and € that only
  Windows-1252 (of the legacy code pages here) can hold.
* **Unicode sample**: the Latin sample plus a line with ẞ (U+1E9E), Ω and 😀 (U+1F600, a surrogate pair in
  UTF-16), none of which Windows-1252 can represent.

To make files like these from your own text, use **Test files…** in the app.
