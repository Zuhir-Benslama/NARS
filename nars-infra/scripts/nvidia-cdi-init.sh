#!/bin/sh
# Generates /etc/cdi/nvidia.yaml for the host GPU. Runs once at node boot
# (systemd oneshot, Before=containerd.service).
#
# The host GPU is donated into this node by kind extraMounts (NARS_GPU=1):
#   /dev/nvidia*               device nodes (world-open 0666, rootless-friendly)
#   /opt/nvidia/driver         curated NVIDIA driver libs + nvidia-smi
#                              (host data/nvidia/driver, built by
#                               scripts/nvidia-driver-bundle.sh)
#
# Safe to run on GPU-less nodes: exits 0 without writing a spec, so the node
# stays a normal CPU worker.
set -eu

SPEC=/etc/cdi/nvidia.yaml
DRIVER_ROOT=/opt/nvidia/driver
mkdir -p /etc/cdi

if [ ! -e /dev/nvidia0 ]; then
    echo "nvidia-cdi-init: no /dev/nvidia0 — GPU-less node, skipping" >&2
    rm -f "$SPEC"
    exit 0
fi

rm -f "$SPEC"
export PATH="$DRIVER_ROOT/bin:$PATH"
export LD_LIBRARY_PATH="$DRIVER_ROOT/lib64"
# --dev-root=/ is REQUIRED: the localizer joins dev-root with the absolute
# device path, so dev-root=/dev + /dev/nvidia0 = /dev/dev/nvidia0 (not found).
if ! nvidia-ctk cdi generate \
        --driver-root="$DRIVER_ROOT" \
        --dev-root=/ \
        --output="$SPEC" 2>/etc/cdi/nvidia-cdi.err; then
    echo "nvidia-cdi-init: nvidia-ctk cdi generate FAILED:" >&2
    cat /etc/cdi/nvidia-cdi.err >&2 || true
    exit 0
fi
chmod 0644 "$SPEC"
echo "nvidia-cdi-init: wrote $SPEC ($(wc -c <"$SPEC") bytes)"

# Second spec: vendor k8s.device-plugin.nvidia.com. The k8s-device-plugin
# (cdi-annotations list strategy) annotates allocations as
# k8s.device-plugin.nvidia.com/gpu=GPU-<uuid>; containerd resolves that from a
# spec file, so a spec MUST exist for that vendor. Pre-generating it here also
# guarantees the pods get correct hostPaths (/dev/nvidia*, /opt/nvidia/driver…)
# and createContainer symlink hooks. The plugin's own generator (which lacks
# symlink support and writes device nodes under the driver root) is neutralized
# by mounting this dir read-only into the daemonset.
PLUGIN_SPEC=/var/run/cdi/k8s.device-plugin.nvidia.com-gpu.yaml
mkdir -p /var/run/cdi
if ! nvidia-ctk cdi generate \
        --driver-root="$DRIVER_ROOT" \
        --dev-root=/ \
        --vendor=k8s.device-plugin.nvidia.com \
        --output="$PLUGIN_SPEC" 2>>/etc/cdi/nvidia-cdi.err; then
    echo "nvidia-cdi-init: plugin-vendor spec generation FAILED:" >&2
    tail -5 /etc/cdi/nvidia-cdi.err >&2 || true
else
    chmod 0644 "$PLUGIN_SPEC"
    echo "nvidia-cdi-init: wrote $PLUGIN_SPEC ($(wc -c <"$PLUGIN_SPEC") bytes)"
fi