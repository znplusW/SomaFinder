# Usage

[简体中文](USAGE.zh-CN.md) · [Home](../README.en.md)

## Search and filters

Select folders, enter a query, and press Enter or Search. Hybrid mode combines lexical and semantic retrieval; keyword mode is useful for exact names/source words; semantic mode requires Soma models and a vector index.

File-type presets filter returned candidates. Search breadth is the total candidate budget, initially 50, distributed by indexed file counts with a minimum per participating group. It is not a count of every matching file on disk.

Removing folders filters the current results. Adding folders beyond the original scope offers Search current scope. Name/path/size/type/date sorting reorders returned files; Soma ordering preserves backend relevance.

## Results, summaries and previews

Switch between details, summary cards and masonry without another search. Double-click opens the original file; the context menu can reveal it in Explorer or copy its path.

Summaries use Soma's original text. Matching words can be bold or yellow. Image previews support pinch zoom and panning; summaries support text zoom and touch scrolling. Masonry supports pinch or Ctrl+wheel thumbnail scaling.

PDF thumbnails use a reliable matching page or fall back to page one. Text/Office files can show located excerpts, without claiming original pagination when unavailable. Video timing/frames require a verifiable mapping, otherwise a cover is shown; this is not a full video player.

## Panes and windows

Drag dividers to resize adjacent panes. For right-side panes, left arrows expand and right arrows collapse. The group control restores the previous layout. The left folder-tree control keeps its own direction convention.

As the window narrows, the rightmost pane yields first; summary/preview can become tabs. Narrower layouts prioritize results and the visible folder tree. Widening restores preferences.

Detach a summary/preview pane with its Independent window button. It stays above its own search window; X docks it, and closing the parent closes it. New search windows can be opened from the menu. The global hotkey can recall the latest window or create a new one; the initial shortcut is Alt+E.

## Indexing and Exit

Settings controls persistent root folders and exclusions. Finder groups new roots by physical storage and checks volume identity, offline media and relocation; ambiguous storage blocks automatic indexing rather than trusting a reused drive letter. Relocation updates a moved/renamed root path but does not promise every existing extraction can be reused.

Manual update incrementally synchronizes the configured scope. Rebuild clears and recreates the selected index and requires confirmation; normal upgrades do not require it.

Timed pauses survive restarts until expiry. Tray Pause until next launch lasts for the current process session. Resume clears pauses but still respects power/memory policies. Lower priority is not a hard GPU or memory quota.

Optional model memory reclaim is a separate setting and is off by default. It requires both available physical RAM below 1 GiB for two continuous minutes and at least 15 minutes with no searches, indexing or queued work. The timers overlap. Memory recovery resets the pressure timer; actual new work resets idleness. A cancelled window wait does not make unfinished backend work idle. Status refreshes do not reset idleness. Resume or a gap in observations requires fresh evidence.

The separate paused-indexing option is also off by default and does not require low RAM. Its one-minute timer starts only after indexing is confirmed stopped. Before every release Finder verifies that indexing is still paused, no index writer is running and no Finder request remains active. Only owned VISION, OCR_CLEANUP and OCR services are released; EMBED, EXPAND and RERANK stay available. ASR already exits with its per-file transcription/index process; unrelated transcription processes are never guessed idle. Resume or pause expiry cancels pending reclaim, and sleep or observation gaps require fresh confirmation. An accepted single unload finishes before resumed indexing starts. Later image/audio/video indexing loads or starts components on demand, so initial extraction can take longer. Model files and completed text/transcript/vector indexes are retained.

Only verified local llama.cpp model services started by this Finder instance are eligible. Borrowed, unidentifiable or unsupported services are skipped. Model worker computation resets the idle window and busy slots block unloading. Model files and indexes are retained. Memory recovery alone does not reload models; actual requests do. Settings displays the result. Running indexing always blocks proactive reclaim.

X normally closes a search window and keeps the tray app running. Tray Exit also stops Finder-managed backends, with confirmation during indexing. Exiting does not clear completed indexes.
