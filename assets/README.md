# Embedded dependency

`capstone.dll.gz` is the gzip-compressed Windows x64 dynamic library from
the official PyPI `capstone==5.0.6` wheel. It is embedded during compilation.

Uncompressed DLL SHA-256:

```text
651096794b3c3c2a1df24345e22844a0311fca56a4f74ea1cfa29107983760e1
```

The same value is checked by the runtime before loading the DLL.
License: [BSD notice](../licenses/CAPSTONE-LICENSE.txt).
