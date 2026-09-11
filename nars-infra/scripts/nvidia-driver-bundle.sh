#!/bin/sh
# Builds a curated NVIDIA driver bundle for the kind GPU node (NARS_GPU=1).
#
# Copies ONLY the host's NVIDIA driver libraries (no glibc, no libstdc++ …) into
#   <NVIDIA_DRIVER_DIR>/lib64
# and recreates the public symlink names (libcuda.so.1, libnvidia-ml.so, …).
#
# Why: the CDI spec that boots GPU pods sets LD_LIBRARY_PATH to the driver
# lib dir. If we bind-mounted the whole host /usr/lib64 (as NVIDIA docs do for
# rootless Docker), a *foreign* host libc would shadow each pod's own libc and
# every pod would crash with `undefined symbol … GLIBC_PRIVATE`. Restricting the
# bundle to NVIDIA libraries keeps pods healthy while the generation tool
# (nvidia-ctk cdi generate) still finds every library listed by the driver.
#
# Bundle layout (mirrors an NVIDIA driver "root"):
#   <NVIDIA_DRIVER_DIR>/lib64/libcuda.so.1 -> libcuda.so.<ver>
#   <NVIDIA_DRIVER_DIR>/lib64/libnvidia-*.so*
#   <NVIDIA_DRIVER_DIR>/lib64/bin/          (reserved; nvidia-smi not required)
#
# Safe to re-run (idempotent) — recreated from scratch each time, driver updates
# are picked up automatically. Callers must own the output dir (rootless docker
# below needs the files readable, nothing more).
set -eu

OUT="${1:-data/nvidia/driver}"
SRC="${NVIDIA_DRIVER_LIB_DIR:-/usr/lib64}"

srclibs=""
# Every NVIDIA .so the driver ships (versioned + symlink aliases).
for pat in libcuda.so* libnvidia-*.so* libnvoptix.so*; do
    for f in "$SRC"/$pat; do
        [ -e "$f" ] && srclibs="$srclibs $f"
    done
done

# Deduplicate real files (symlink aliases all resolve to the same target).
reals=""
for l in $srclibs; do
    real=$(readlink -f "$l" 2>/dev/null || echo "$l")
    case " $reals " in *" $real "*) ;; *) reals="$reals $real" ;; esac
done

rm -rf "$OUT"
mkdir -p "$OUT/lib64" "$OUT/bin"

for real in $reals; do
    cp -L -p "$real" "$OUT/lib64/$(basename "$real")"
done

# Recreate the unversioned/versioned symlink names (libcuda.so, libcuda.so.1, …).
for l in $srclibs; do
    [ -L "$l" ] || continue
    alias=$(basename "$l")
    real=$(basename "$(readlink -f "$l" 2>/dev/null || echo "$l")")
    [ -f "$OUT/lib64/$real" ] && [ "$alias" != "$real" ] && ln -sf "$real" "$OUT/lib64/$alias"
done

count=$(find "$OUT/lib64" -type f | wc -l)
if [ -x /usr/bin/nvidia-smi ]; then
    cp -L /usr/bin/nvidia-smi "$OUT/bin/nvidia-smi"
else
    echo "  (nvidia-smi not found in /usr/bin; CDI nvml mode only)"
fi

# NVIDIA Container Toolkit tooling, extracted from the GPU node image. These
# live at fixed paths inside the image; the bundle keeps them next to the
# driver so the node's /opt/nvidia/driver can serve as both driver root AND
# the CDI hook location (nvidia-ctk) for device-plugin generated specs.
NODE_IMAGE="${NVIDIA_GPU_NODE_IMAGE:-nars/gpu-node:1.32.2}"
for spec in \
    "bin/nvidia-ctk|/usr/bin/nvidia-ctk" \
    "lib64/libnvidia-container.so.1.20.0|/usr/lib/x86_64-linux-gnu/libnvidia-container.so.1.20.0"; do
    rel="${spec%%|*}"
    imgpath="${spec##*|}"
    if docker run --rm --entrypoint true "$NODE_IMAGE" 2>/dev/null &&
       docker run --rm --entrypoint cat "$NODE_IMAGE" "$imgpath" > "$OUT/$rel" 2>/dev/null; then
        chmod 755 "$OUT/$rel"
        echo "  toolkit → $OUT/$rel"
    else
        echo "  WARNING: could not extract $imgpath from $NODE_IMAGE (CDI hook of plugin-generated specs may fail)"
    fi
done
[ -f "$OUT/lib64/libnvidia-container.so.1.20.0" ] && ln -sf libnvidia-container.so.1.20.0 "$OUT/lib64/libnvidia-container.so.1"

count=$(find "$OUT/lib64" -type f | wc -l)
echo "✓ NVIDIA driver bundle: $count driver/toolkit libraries → $OUT"
du -sh "$OUT"