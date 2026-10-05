# kan-rhythm ⚡

**You kan!** Motor DSP híbrido en C++20 para detección de ritmo y afinación en tiempo real, con latencia de buffer de **~2.66 ms** (128 muestras @ 48 kHz).

Sin burocracia: CMake descarga [miniaudio](https://github.com/mackron/miniaudio) automáticamente, abre tu micrófono en modo de ultra-baja latencia, pasa los datos por un **Ring Buffer Lock-Free (SPSC)** sin bloquear nunca el hilo de audio, y detecta en tiempo real:

- 🥁 **Onset** — el golpe rítmico instantáneo (~3 ms)
- 🎵 **Nota musical** — pitch monofónico por autocorrelación YIN + desviación en **cents** respecto a A4 = 440 Hz (~21 ms post-ataque)

## Arquitectura

```
Micrófono (callback RT, cero bloqueos/allocs)
        │
        ▼
LockFreeRingBuffer<float, 16384>   ← cola SPSC atómica (src/libkan/ring_buffer.hpp)
        │
        ▼
HybridDetector                     ← pipeline híbrido (src/libkan/dsp_detector.hpp)
  ├── process_onset_block()        ← VÍA RÁPIDA: salto de energía/derivada HFC (~2.6 ms/bloque)
  └── detect_pitch()               ← VÍA ARMÓNICA: YIN ligero + interpolación parabólica (~20 ms)
        │
        ▼
CLI en tiempo real (src/main.cpp)  ← ⚡ ONSET! → ✅ HIT CONFIRMADO (nota, Hz, cents)
```

## Estructura

```
kan-rhythm/
├── CMakeLists.txt
├── LICENSE                 (MIT)
├── README.md
└── src/
    ├── libkan/
    │   ├── ring_buffer.hpp   Comunicación sin bloqueos entre Audio y Juego
    │   └── dsp_detector.hpp  Pipeline híbrido: Onset rápido + Pitch en cents
    └── main.cpp              CLI de prueba en tiempo real con tu micrófono
```

## Requisitos

- CMake ≥ 3.20
- Compilador C++20 (GCC/Clang/MSVC)
- Conexión a internet la primera vez (FetchContent descarga miniaudio, MIT-0)
- Linux: ALSA (paquete `libasound2-dev` en Debian/Ubuntu)

## Compilar y ejecutar

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
./build/kan_cli
```

Toca una cuerda de guitarra, una tecla de piano o sopla una flauta frente al micrófono:

```
⚡ [ONSET! 300/PERFECT] Golpe detectado (<3ms) -> Evaluando tono...
✅ [HIT CONFIRMADO en +22.3 ms] Nota:  A4 | Freq:  441.2 Hz | Desviacion: +4.7 cents
🥁 [PERCUSIVO / MUTE] Ataque rítmico sin tono sostenido
```

## Licencia

MIT — ver [LICENSE](LICENSE). miniaudio es MIT-0 / dominio público.
