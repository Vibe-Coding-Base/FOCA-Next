# Security

## Reporting a vulnerability

Please report security issues privately via GitHub's **Report a vulnerability** (Security tab) rather
than opening a public issue. Include steps to reproduce and, if possible, a sample input. We aim to
acknowledge reports within a few days.

## Threat model

FOCA Next analyses documents **downloaded from servers it does not control**. A crafted document must
never be able to compromise or hang the tool. The document parsers (`Foca.Metadata`) are built
defensively:

- **XML** is parsed through `SafeXml`: DTD processing is disabled (no entity expansion / "billion
  laughs"), no resolver is set (no external entity / XXE, no file or network access), and document
  size is bounded.
- **Archives** (OpenDocument, OOXML) are read through `SafeZip` / `BoundedStream`, which cap the
  inflated size of each entry and the number of entries (zip bombs). `.svgz` is inflated with the
  same cap (gzip bombs).
- **Binary structures** (OLE compound files, Word tables, EXIF thumbnails) validate lengths and
  offsets against the data actually available before allocating, instead of trusting header fields
  that could request gigabytes. Sector chains are protected against cycles.
- **Regular expressions** that scan whole files have a timeout (ReDoS), set process-wide and per
  pattern.
- **Recursion** (old document versions embedded in documents) is depth- and count-bounded.
- **Downloads** are size-capped and written with sanitised file names confined to the project folder;
  URL-derived names cannot escape it.

These behaviours are covered by tests in `tests/Foca.Metadata.Tests` (XXE, billion laughs, zip/gzip
bombs, oversized length fields, cyclic structures, and seeded mutation fuzzing of every corpus
document).

## Secrets

API keys are stored encrypted at rest: DPAPI (current user) on Windows, and AES with a key derived
from the machine and user elsewhere. The plain values exist only in memory while the app runs. This
keeps keys out of plain sight; it is not a substitute for full-disk encryption.

## Network

Requests to **audited hosts** (fingerprinting, downloads) deliberately accept any TLS certificate,
because those hosts routinely present self-signed or expired certificates. Requests to **third-party
services** (search engines, the Internet Archive, crt.sh) keep full certificate validation.

## Dependencies

The build fails on any dependency with a known vulnerability (`NuGetAudit` with warnings-as-errors),
and CI runs the audit on every push.
