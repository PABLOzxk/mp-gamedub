# MP GameDub

Legenda e voz em português do Brasil, em tempo real, para as falas em inglês de jogos de PC.

Este repositório está na **Fase 0: medir antes de construir**. O que existe aqui é um programa de teste
(`MpGameDubTeste.exe`) que responde, com números do PC de quem vai usar, se a ideia é viável:

- quanto tempo passa entre o fim de uma fala no jogo e o começo da voz traduzida;
- quanto o jogo perde de FPS com a IA ligada, e quanta memória da placa ela usa;
- se a tradução e a voz são boas o bastante para jogar uma hora.

As instruções de uso estão em [LEIA-ME.txt](LEIA-ME.txt), que vai junto no .zip.

## Metas da Fase 0

| Meta | Como é medida |
|---|---|
| 95% das falas curtas (até 4 s) com a voz começando até 3,5 s depois do fim da fala | o programa mede |
| 1% low do jogo cai no máximo 10% com a IA ligada | o usuário mede (FrameView ou benchmark do jogo) e informa |
| Pelo menos 15 de 20 falas compreensíveis | o usuário ouve o `dublado.wav` |
| Voz aceitável para jogar uma hora | o usuário ouve o `dublado.wav` |

Só se passarem as quatro vale começar a Fase 1 (legenda PT-BR com interface).

## Como funciona

```
som do jogo (só o processo dele, pelo loopback de processo do Windows)
  → 16 kHz mono → detector de fala Silero VAD → trechos de fala
  → Whisper (whisper.cpp, Vulkan ou CPU) → filtro de alucinação
  → Opus-MT en→pt-BR (ONNX, CPU) → voz Kokoro (ONNX, DirectML ou CPU)
  → saída de som, com o volume do jogo abaixado enquanto a voz fala
```

Regras que valem para todo o código:

- Nada de injetar no jogo, ler ou alterar memória, mexer em executáveis ou contornar anti-cheat.
  A captura é a mesma API pública que programas de gravação usam.
- Nada de administrador para o uso normal.
- Nenhum áudio é gravado sem o usuário pedir. Nada vai para a nuvem nesta fase.
- Todo modelo baixado tem origem, tamanho e SHA-256 fixos no código; se não bater, é apagado.
- Toda etapa tem prazo; uma fala atrasada demais é descartada para o atraso não acumular.

## Estrutura

| Pasta | O que tem |
|---|---|
| `src/MpGameDub.Core` | Áudio, detector de fala, filtro, fila e descarte, simulador de tempo real, relatórios, downloads com hash. Sem dependência de Windows. |
| `src/MpGameDub.Engines` | Silero VAD, Whisper, Opus-MT e Kokoro atrás das interfaces do Core. |
| `src/MpGameDub.Windows` | Captura do processo do jogo, volume do jogo, saída da voz, medição de VRAM/CPU, escolha da placa. |
| `src/MpGameDub.Teste` | O programa de console da Fase 0. |
| `tests/MpGameDub.Tests` | Testes do Core (rodam em qualquer sistema). |
| `tests/MpGameDub.Engines.Tests` | Testes com os modelos de verdade. O Silero sempre roda; Whisper e Kokoro com `MPGAMEDUB_TESTES_PESADOS=1`; Opus-MT com `MPGAMEDUB_OPUSMT=<pasta>`. |
| `ferramentas` | Conversão do Opus-MT para ONNX (roda no CI). |
| `assets/exemplo.wav` | 50 s de falas em inglês com ruído, música e explosão, para testar sem jogo. |

## Compilar e testar

Precisa do .NET 10 SDK.

```
dotnet test tests/MpGameDub.Tests
dotnet test tests/MpGameDub.Engines.Tests
dotnet publish src/MpGameDub.Teste -c Release -o publicado
```

O CI (`.github/workflows/ci.yml`) roda os testes no Linux e no Windows, converte o Opus-MT, roda o
exemplo de ponta a ponta na CPU e guarda a pasta do programa como artefato (o GitHub entrega zipada; baixar
exige entrar no GitHub). O workflow `programa.yml`, disparado à mão a partir da main, publica o mesmo programa
numa release, cujo link baixa sem login.
O workflow `modelos.yml` publica o Opus-MT convertido numa release, só quando disparado à mão. A publicada é
`modelos-opus-mt-v1`; o endereço, o tamanho e o SHA-256 ficam em `ModelCatalog.cs`.

## Licenças

Código: MIT (ver [LICENSE](LICENSE)), igual ao ACHIEVEMENTS MP. Modelos e bibliotecas de terceiros: [NOTICE.md](NOTICE.md).
