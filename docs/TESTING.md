# Testes e desenvolvimento

Como compilar e testar o SDK para .NET (C#) a partir da fonte. O contrato fixado e a versão estão descritos em [INTEGRATION.md](INTEGRATION.md).

```sh
scripts/verify.sh
```

O script compila e executa `tests/SocketTests` (HTTP/TLS loopback com fixtures sintéticas), empacota o SDK em um feed local e repete a suíte com os consumidores de linguagem e com o exemplo de homologação contra uma fixture loopback; não faz chamadas fiscais reais.
