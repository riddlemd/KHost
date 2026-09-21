#!/bin/bash
# mkloop.sh <name> <lavfi-source> [post-filter]
# Builds a seamless LOOP-second background: generate LOOP+XF seconds, then crossfade
# the trailing XF back over the opening XF so the last frame lands where the first began.
set -e
NAME=$1; SRC=$2; POST=${3:-null}
LOOP=15; XF=5; W=1920; H=1080; R=30; SEG=2
TOTAL=$((LOOP+XF))

ffmpeg -hide_banner -loglevel error \
 -f lavfi -i "$SRC" -t $TOTAL \
 -filter_complex "\
  [0:v]${POST},format=yuv420p,setsar=1[src];\
  [src]split=2[a][b];\
  [a]trim=0:${LOOP},setpts=PTS-STARTPTS[head];\
  [b]trim=${LOOP}:${TOTAL},setpts=PTS-STARTPTS[tail];\
  [head]split=2[h1][h2];\
  [h1]trim=0:${XF},setpts=PTS-STARTPTS[h_in];\
  [h2]trim=${XF}:${LOOP},setpts=PTS-STARTPTS[h_rest];\
  [tail][h_in]blend=all_expr='A*(1-(T/${XF}))+B*(T/${XF})'[mixed];\
  [mixed][h_rest]concat=n=2:v=1:a=0[v]" \
 -map "[v]" -r $R -c:v h264_videotoolbox -b:v 5M \
 -g $((SEG*R)) -keyint_min $((SEG*R)) -force_key_frames "expr:gte(t,n_forced*${SEG})" \
 -movflags +faststart -y "${NAME}.mp4"

KF=$(ffprobe -hide_banner -loglevel error -select_streams v -skip_frame nokey \
     -show_entries frame=pts_time -of csv=p=0 "${NAME}.mp4" | tr '\n' ' ')
SZ=$(du -h "${NAME}.mp4" | cut -f1)
echo "${NAME}: ${SZ}  keyframes: ${KF}"
