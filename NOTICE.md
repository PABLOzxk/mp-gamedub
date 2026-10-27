# Licenças de terceiros

O MP GameDub usa estes componentes. Os modelos não vêm no pacote: o programa baixa cada um da origem
listada, confere o tamanho e o SHA-256 (fixos em `src/MpGameDub.Core/Models/ModelCatalog.cs`) e apaga o
arquivo se não bater.

## Modelos

| Modelo | Origem | Licença |
|---|---|---|
| Silero VAD 6.2.3 | github.com/snakers4/silero-vad | MIT |
| Whisper (small, medium, large-v3-turbo) em formato ggml | huggingface.co/ggerganov/whisper.cpp, pesos da OpenAI | MIT |
| Kokoro-82M v1.0 (ONNX) | github.com/Lyrcaxis/KokoroSharpBinaries, modelo de hexgrad/Kokoro-82M | Apache-2.0 |
| Vozes Kokoro pf_dora, pm_alex, pm_santa | pacote KokoroSharp (pasta `voices`, com a licença) | Apache-2.0 |
| Opus-MT tc-big en-pt | huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-pt | CC-BY-4.0 |

Atribuição do Opus-MT (exigida pela CC-BY-4.0): modelo de tradução de Jörg Tiedemann e equipe, Language
Technology Research Group, Universidade de Helsinque (projetos OPUS-MT e Tatoeba Translation Challenge).
Modificação feita aqui: conversão para ONNX com o Optimum e quantização para 8 bits com o ONNX Runtime
(`ferramentas/exportar-opus-mt.py`). Licença: https://creativecommons.org/licenses/by/4.0/

## Bibliotecas

| Biblioteca | Licença |
|---|---|
| .NET | MIT |
| NAudio 3.3 | MIT |
| Whisper.net e whisper.cpp | MIT |
| ONNX Runtime | MIT |
| DirectML | Licença de redistribuição da Microsoft (vem no pacote Microsoft.AI.DirectML) |
| Microsoft.ML.Tokenizers | MIT |
| KokoroSharp | MIT |
| MisakiSharp | Apache-2.0 |
| NumSharp | Apache-2.0 |
| OpenTK | MIT |

## Áudio de exemplo

`assets/exemplo.wav` foi gerado para este projeto: falas originais em inglês com as vozes Kokoro
am_michael, af_heart e bm_george (Apache-2.0) e ruído, passos, música e explosão sintetizados.
