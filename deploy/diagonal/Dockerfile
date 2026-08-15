# Derived image for DiagonalSignalerBot: adds the font stack needed to render
# the SVG trade-result reports (ReportGenerator uses Svg.Skia -> SkiaSharp).
#
# The base image has NO fonts and NO libfontconfig/libfreetype, so SkiaSharp
# would fall back to a serif default instead of rendering the report text.
FROM ghcr.io/spotware/ctrader-console:latest

# fontconfig (font discovery for SkiaSharp) + TeX Gyre Heros (free Helvetica
# clone, the family the SVG template references) + DejaVu (glyph fallback).
#
# IMPORTANT #1 (fonts path): Debian's fonts-texgyre installs into
# /usr/share/texmf/fonts/opentype/public/tex-gyre/, so the symlink below makes
# the family visible under the standard /usr/share/fonts tree as well.
#
# IMPORTANT #2 (native build flavor): libSkiaSharp.so from
# SkiaSharp.NativeAssets.Linux.NoDependencies is NOT linked against fontconfig
# (ldd shows only pthread/dl/m/c). Its custom font manager can match by family
# name (MatchFamily/FromFamilyName work) but has NO charset matching:
# SKFontManager.MatchCharacter() always returns null there. Svg.Skia 2.0.0.1
# resolves the typeface of every SVG text run via MatchCharacter
# (SkiaAssetLoader.FindTypefaces), so with the NoDependencies build every <text>
# silently renders in the default serif face (DejaVu Serif), ignoring
# font-family and font-weight. Verified empirically in this image.
# => We MUST use the regular SkiaSharp.NativeAssets.Linux build (linked against
# libfontconfig, which the fontconfig package above provides).
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        fontconfig \
        fonts-texgyre \
        fonts-dejavu \
    && mkdir -p /usr/share/fonts/opentype \
    && ln -sfn /usr/share/texmf/fonts/opentype/public/tex-gyre /usr/share/fonts/opentype/tex-gyre \
    && rm -rf /var/lib/apt/lists/*

# --- Linux native libs for SkiaSharp (Svg.Skia -> SkiaSharp -> libSkiaSharp.so) ---
# The cBot resolves SkiaSharp 3.0.0-preview.5.4 whose native deps only cover
# Win/macOS, so the linux-x64 .so is not shipped in the .algo. Download it from
# NuGet and install into the system library path so `dlopen` finds it at runtime.
ARG SKIA_VERSION=3.0.0-preview.5.4
ARG HARFBUZZ_VERSION=7.3.0.2
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl unzip \
    && mkdir -p /tmp/skia \
    && curl -fsSL --retry 6 --retry-delay 3 --retry-all-errors --connect-timeout 30 "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux/${SKIA_VERSION}/skiasharp.nativeassets.linux.${SKIA_VERSION}.nupkg" -o /tmp/skia/skia.nupkg \
    && curl -fsSL --retry 6 --retry-delay 3 --retry-all-errors --connect-timeout 30 "https://api.nuget.org/v3-flatcontainer/harfbuzzsharp.nativeassets.linux/${HARFBUZZ_VERSION}/harfbuzzsharp.nativeassets.linux.${HARFBUZZ_VERSION}.nupkg" -o /tmp/skia/hb.nupkg \
    && unzip -o -j /tmp/skia/skia.nupkg 'runtimes/linux-x64/native/*.so' -d /usr/lib/x86_64-linux-gnu/ \
    && unzip -o -j /tmp/skia/hb.nupkg 'runtimes/linux-x64/native/*.so' -d /usr/lib/x86_64-linux-gnu/ \
    && rm -rf /tmp/skia \
    && rm -rf /var/lib/apt/lists/* \
    && ldconfig

# Entrypoint: switches live (run) vs backtest (backtest) via the MODE env var.
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

RUN fc-cache -f

ENTRYPOINT ["/entrypoint.sh"]
