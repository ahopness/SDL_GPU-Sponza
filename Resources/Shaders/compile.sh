#!/usr/bin/env bash

# ADAPTED FROM https://github.com/TheSpydog/SDL_gpu_examples/blob/main/Content/Shaders/Source/compile.sh
# Requires shadercross CLI installed from SDL_shadercross
set -euo pipefail

output_dir="${1:-Compiled}"
mkdir -p -- "$output_dir"
shopt -s nullglob
shaders=(*.vert.hlsl *.frag.hlsl *.comp.hlsl)
for filename in "${shaders[@]}"; do
    for format in spv msl dxil; do
        output="$output_dir/${filename%.hlsl}.$format"
        echo "Compiling $filename -> $output"
        shadercross "$filename" -s HLSL -e main -o "$output"
    done
done
