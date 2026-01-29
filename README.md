# G19 Performance Monitor - VRAM & AI Edition

A high-visibility, single-page hardware monitoring dashboard designed specifically for the legendary **Logitech G19** (and G19s) keyboards.

This modern refactor is optimized for users running local LLMs, providing real-time VRAM tracking and service status monitoring directly on your keyboard's LCD.

![Dashboard Close-up](images/lcd.jpg)
![Keyboard Context](images/keyboard.jpg)

## Features

- **Dual Detail Graphs**: 80px high history graphs for CPU/RAM and GPU/VRAM.
- **VRAM Intelligence**: Decoupled trackers for dedicated GPU memory, identifying exactly how much memory your local models are consuming.
- **AI/LLM Support**: Built-in health probes for common local AI services:
  - Jina Reranker
  - Qwen3 Embedding
  - FunctionGemma / Ollama / vLLM (Configurable)
- **Comprehensive storage**: Multi-drive summary (C, D, E, F, L, etc.) in high-contrast orange.
- **Dead/Alive Status**: Visual indicators (Cyan/Grey) for service uptime.

## Requirements

- **Logitech Gaming Software v9.02.65**: (CRITICAL) This applet requires version 9.02.65 for stable LCD polling and rendering. Newer G-Hub versions may not support the legacy LCD SDK correctly.
- **.NET Framework 4.8**: The application is built for the Windows .NET Framework 4.8.
- **NVIDIA GPU**: Required for high-precision VRAM/Utilization via NVML. Supports fallback to DXGI/Performance Counters for other GPUs.

## Installation

1. Download the latest `bin/Release/net48/G19PerformanceMonitorVRAM.exe`.
2. Ensure Logitech Gaming Software 9.02.65 is running.
3. Launch the `.exe`.
4. (Optional) Run on Startup: Check the "Run on Startup" option in the LGS Applet settings or place a shortcut in your Startup folder.

## Configuration

The application automatically creates a configuration file at:
`%AppData%\G19PerformanceMonitor\settings.json`

You can customize the following:

- **LlmEndpoints**: Add your own local API endpoints (GET or POST) to track.
- **Colors**: Adjust CPU/RAM/VRAM hex codes.
- **Intervals**: Fine-tune polling and rendering speeds.

## Credits

Building on the legacy of G19 monitoring apps, refactored for the age of Local AI.
