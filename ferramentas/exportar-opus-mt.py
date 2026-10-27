"""Exporta o Opus-MT tc-big en-pt para ONNX, quantiza para 8 bits e grava as referências do teste do C#.

Uso: python ferramentas/exportar-opus-mt.py <pasta-de-saida>

Saída: encoder_model.onnx, decoder_model.onnx, decoder_with_past_model.onnx (8 bits), source.spm, target.spm,
vocab.json, config.json, LICENCA.txt e referencia.json. A referência traz, para cada frase, os ids que o tokenizador
do transformers gera e a tradução gulosa feita com os mesmos arquivos ONNX, pelo mesmo algoritmo do
OpusMtTranslator.cs; o teste do C# tem que bater com as duas.
"""

import json
import shutil
import sys
import tempfile
from pathlib import Path

import numpy as np
import onnxruntime as ort
from onnxruntime.quantization import QuantType, quantize_dynamic
from optimum.exporters.onnx import main_export
from transformers import MarianMTModel, MarianTokenizer

MODELO = "Helsinki-NLP/opus-mt-tc-big-en-pt"
REVISAO = "main"
CODIGO = ">>pob<<"
ARQUIVOS = ["encoder_model.onnx", "decoder_model.onnx", "decoder_with_past_model.onnx"]

# Falas no estilo de jogo, curtas e longas, com nomes próprios, números e gíria.
FRASES = [
    "Get down! They're coming from the bridge.",
    "I told you, we don't have time for this.",
    "The reactor will overload in five minutes. Move!",
    "Captain Price wants us at the extraction point before sunrise.",
    "Thanks, kid. I owe you one.",
    "If you see the Witcher, tell him the contract is cancelled.",
    "Reload! I'm out of ammo!",
    "We found the key, but the door on the third floor is still locked.",
]


def limite_de_passos(tamanho: int) -> int:
    return min(512, 2 * tamanho + 16)  # igual ao OpusMtTranslator.MaxSteps


def traduzir_onnx(pasta: Path, ids: list[int], config: dict) -> list[int]:
    opcoes = ort.SessionOptions()
    opcoes.intra_op_num_threads = 4
    enc = ort.InferenceSession(str(pasta / "encoder_model.onnx"), opcoes, providers=["CPUExecutionProvider"])
    dec = ort.InferenceSession(str(pasta / "decoder_model.onnx"), opcoes, providers=["CPUExecutionProvider"])
    past_dec = ort.InferenceSession(str(pasta / "decoder_with_past_model.onnx"), opcoes, providers=["CPUExecutionProvider"])

    entrada = np.array([ids], dtype=np.int64)
    mascara = np.ones_like(entrada)
    oculto = enc.run(None, {"input_ids": entrada, "attention_mask": mascara})[0]

    pad, eos = config["pad_token_id"], config["eos_token_id"]
    proximo = config["decoder_start_token_id"]
    passado: dict[str, np.ndarray] = {}
    saida: list[int] = []
    for passo in range(limite_de_passos(len(ids))):
        sessao = dec if passo == 0 else past_dec
        nomes = [i.name for i in sessao.get_inputs()]
        feed = {"input_ids": np.array([[proximo]], dtype=np.int64), "encoder_attention_mask": mascara}
        if "encoder_hidden_states" in nomes:
            feed["encoder_hidden_states"] = oculto
        if passo > 0:
            feed.update({n: passado[n] for n in nomes if n.startswith("past_key_values.")})
        nomes_saida = [o.name for o in sessao.get_outputs()]
        resultados = dict(zip(nomes_saida, sessao.run(None, feed)))
        for nome, valor in resultados.items():
            if nome.startswith("present."):
                passado["past_key_values." + nome[len("present."):]] = valor
        logits = resultados["logits"][0, -1].copy()
        logits[pad] = -np.inf
        proximo = int(np.argmax(logits))
        if proximo == eos:
            break
        saida.append(proximo)
    return saida


def main() -> None:
    destino = Path(sys.argv[1]).resolve()
    destino.mkdir(parents=True, exist_ok=True)
    tokenizador = MarianTokenizer.from_pretrained(MODELO, revision=REVISAO)

    with tempfile.TemporaryDirectory() as tmp:
        bruto = Path(tmp) / "fp32"
        # Sem pós-processamento: mantém decoder_model e decoder_with_past_model separados (sem o If do modelo fundido).
        main_export(MODELO, output=bruto, task="text2text-generation-with-past", revision=REVISAO,
                    no_post_process=True, device="cpu")
        for nome in ARQUIVOS:
            quantize_dynamic(str(bruto / nome), str(destino / nome), weight_type=QuantType.QInt8)
        for nome in ["source.spm", "target.spm", "vocab.json", "config.json"]:
            shutil.copy(bruto / nome, destino / nome)

    config = json.loads((destino / "config.json").read_text(encoding="utf-8"))
    assert config["decoder_start_token_id"] == config["pad_token_id"], "Marian começa pelo token de preenchimento"

    modelo = MarianMTModel.from_pretrained(MODELO, revision=REVISAO).eval()
    referencia = []
    for frase in FRASES:
        ids = tokenizador(f"{CODIGO} {frase}").input_ids
        gerado = traduzir_onnx(destino, ids, config)
        pytorch = modelo.generate(**tokenizador([f"{CODIGO} {frase}"], return_tensors="pt"), num_beams=1, do_sample=False)
        referencia.append({
            "texto": frase,
            "ids": ids,
            "traducao": tokenizador.decode(gerado, skip_special_tokens=True),
            "traducao_pytorch_fp32": tokenizador.decode(pytorch[0], skip_special_tokens=True),
        })
        print(f"{frase}\n  onnx 8 bits: {referencia[-1]['traducao']}\n  pytorch fp32: {referencia[-1]['traducao_pytorch_fp32']}")

    (destino / "referencia.json").write_text(json.dumps(referencia, ensure_ascii=False, indent=2), encoding="utf-8")
    (destino / "LICENCA.txt").write_text(
        "Opus-MT tc-big en-pt, de Helsinki-NLP (https://huggingface.co/Helsinki-NLP/opus-mt-tc-big-en-pt).\n"
        "Licença CC-BY-4.0 (https://creativecommons.org/licenses/by/4.0/).\n"
        "Modificação: convertido para ONNX com o Optimum e quantizado para 8 bits com o ONNX Runtime.\n",
        encoding="utf-8")


if __name__ == "__main__":
    main()
