# Derived image for DiagonalSignalerBot: adds the font stack needed to render
# the SVG trade-result reports (ReportGenerator uses Svg.Skia -> SkiaSharp).
#
# The base image has NO fonts and NO libfontconfig/libfreetype, so SkiaSharp
# cannot resolve "Helvetica Neue LT *" (and text would not render at all).
FROM ghcr.io/spotware/ctrader-console:latest

# fontconfig (font discovery for SkiaSharp) + free metric-compatible fonts.
# "Helvetica Neue LT Com/Pro" is a commercial Linotype font and is NOT in apt;
# fonts.conf aliases it to Liberation Sans (metric-compatible with Helvetica/Arial).
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        fontconfig \
        fonts-liberation \
        fonts-dejavu \
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
    && curl -fsSL "https://api.nuget.org/v3-flatcontainer/skiasharp.nativeassets.linux.nodependencies/${SKIA_VERSION}/skiasharp.nativeassets.linux.nodependencies.${SKIA_VERSION}.nupkg" -o /tmp/skia/skia.nupkg \
    && curl -fsSL "https://api.nuget.org/v3-flatcontainer/harfbuzzsharp.nativeassets.linux/${HARFBUZZ_VERSION}/harfbuzzsharp.nativeassets.linux.${HARFBUZZ_VERSION}.nupkg" -o /tmp/skia/hb.nupkg \
    && unzip -o -j /tmp/skia/skia.nupkg 'runtimes/linux-x64/native/*.so' -d /usr/lib/x86_64-linux-gnu/ \
    && unzip -o -j /tmp/skia/hb.nupkg 'runtimes/linux-x64/native/*.so' -d /usr/lib/x86_64-linux-gnu/ \
    && rm -rf /tmp/skia \
    && rm -rf /var/lib/apt/lists/* \
    && ldconfig

# Map the SVG template's font families to the installed free fonts.
COPY fonts.conf /etc/fonts/local.conf

# Entrypoint: switches live (run) vs backtest (backtest) via the MODE env var.
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

RUN fc-cache -f

ENTRYPOINT ["/entrypoint.sh"]
