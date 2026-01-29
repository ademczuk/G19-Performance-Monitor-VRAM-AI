# G19 Performance Monitor - VRAM & AI Edition

A high-visibility, single-page hardware monitoring dashboard designed specifically for the legendary **Logitech G19** (and G19s) keyboards.

This modern refactor is optimized for users running local LLMs, providing real-time VRAM tracking and service status monitoring directly on your keyboard's LCD.

![Dashboard Close-up](https://raw.githubusercontent.com/ademczuk/G19-Performance-Monitor-VRAM-AI/main/LCD.jpg)
![Keyboard Context](https://raw.githubusercontent.com/ademczuk/G19-Performance-Monitor-VRAM-AI/main/Keyboard.jpg)

## Performance & Thermal Features

- **Dual Detail Graphs**: 80px high history graphs for CPU/RAM and GPU/VRAM.
- **Thermal Monitoring**: Real-time **CPU & GPU Temperature** tracking. 
  - *Note: CPU temperature on Ryzen/Core systems typically requires running the app as **Administrator**.*
- **Improved Stability**: Ultra-fast HTTP probing (1s timeouts) ensures the UI never freezes, even when local LLM services are unresponsive.

## AI & LLM Intelligence

- **VRAM Intelligence**: Decoupled trackers for dedicated GPU memory, identifying exactly how much memory your local models are consuming.
- **Zombie LLM Detection**: Visual indicators (Grey/DEAD status) for services that are configured but currently unresponsive or crashed.
- **Service Health Probes**: Built-in support for:
  - Jina Reranker
  - Qwen3 Embedding
  - FunctionGemma / Ollama / vLLM (Configurable)

## Requirements

- **Logitech Gaming Software v9.02.65**: (CRITICAL) This applet requires version 9.02.65 for stable LCD polling and rendering. Newer G-Hub versions may not support the legacy LCD SDK correctly.
- **.NET Framework 4.8**: The application is built for the Windows .NET Framework 4.8.
- **NVIDIA GPU**: Required for high-precision VRAM/Utilization via NVML. Supports fallback to DXGI/Performance Counters for other GPUs.
- **Administrator Rights**: (Recommended) Required for WMI-based CPU temperature monitoring.

## Installation

1. Download the latest `bin/Release/net48/G19PerformanceMonitorVRAM.exe`.
2. Ensure Logitech Gaming Software 9.02.65 is running.
3. Launch the `.exe`.

## Configuration

The application automatically creates a configuration file at:
`%AppData%\G19PerformanceMonitor\settings.json`

## Credits

Building on the legacy of G19 monitoring apps, refactored for the age of Local AI.
