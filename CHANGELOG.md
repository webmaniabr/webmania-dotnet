# Changelog

## 0.1.0-preview.6

- README reescrito para a integração: requisitos, instalação, credenciais por variável de ambiente, exemplos de emissão, consulta e cancelamento de NF-e e NFS-e em homologação e a lista de métodos por produto. Os detalhes técnicos que estavam no README foram para `docs/INTEGRATION.md`, `docs/TESTING.md`, `docs/OPERATIONS.md` e `examples/homologacao/README.md`, sem mudança de conteúdo. Somente documentação.
- README: o SDK passa a ser apresentado como disponível no repositório público do GitHub, com instalação a partir do clone (`git clone https://github.com/webmaniabr/webmania-dotnet.git`); continua sem publicação em registro de pacotes. Somente documentação: código e contrato não mudam.
- O exemplo de homologação exige `WEBMANIA_LIVE_LEDGER` gravável nos escopos `nfe`, `nfse` e `all` (sai com 77, motivo `ledger`, antes de qualquer requisição), para que um resultado desconhecido fique sempre registrado.
- Regenerado do contrato público atualizado: os totais da lista de classes de imposto (`tributos_federais`, `tributos_estaduais`) aceitam a string vazia que a API devolve para valor não definido; `situacao_tributaria` das classes de imposto tem 2 dígitos para IPI, PIS e COFINS e 2 a 3 dígitos para ICMS; `impostos.reducao` e `impostos.cp` da NFS-e aceitam string ou número.
- O exemplo de homologação trata `reprovado` (status da API 1.0 para NF-e recusada) como terminal.
- O exemplo de homologação exige `WEBMANIA_LIVE_ALLOWED_HOSTS` (sai com 77 antes de ler credenciais ou fazer requisições quando ela falta ou está vazia) e falha a execução quando a consulta depois do cancelamento não mostra `cancelado`.
- Contratos regenerados do OpenAPI canônico e fixados por `contract_digest` (`371a499f74012bb6b41f23642869dee4954e8394dcba7642edc701aedb3dd59e`) em `contracts.lock.json`: 14 módulos, 103 operações, 102 habilitadas. Os payloads de emissão da API 2.0 passam a usar os nomes `<Operação>Payload`.
- Nova operação `FiscalDownloadReportV2Async` (`GET /relatorios/{uuid}` na API 2.0) com `WebmaniaCredentials.ReportCapability()`; um JSON com `error`, mesmo em HTTP 200, é falha.
- `FiscalDownloadReportAsync`: relatórios legados rotulados `text/xml` são classificados pelos bytes (ZIP = XLSX; demais corpos = CSV byte a byte, inclusive Latin-1); XML, HTML ou JSON nesse download é recusado.
- `WebmaniaClientOptions.DnsHelperHost` permite indicar o host .NET do resolvedor de nomes quando o runtime não tem um `dotnet` ao lado.
- O conjunto de capabilities lembradas para redação tem limite fixo; ao atingi-lo, os diagnósticos passam a redigir de forma estrita em vez de crescer.
- XLSX com relacionamentos `TargetMode="External"` deixa de ser recusado (o destino externo nunca é resolvido); marcadores ZIP64 resultam em `Unsupported`.
- `nfeDownloadIbsCbsEventXml` continua indisponível; a mensagem informa que nenhuma requisição foi enviada.
- Novo exemplo neutro de homologação em `examples/homologacao` (NF-e e NFS-e em `ambiente` 2, consulta e cancelamento, saída mascarada), testado contra uma fixture loopback a partir do pacote candidato.
- `scripts/audit_package.py` falha explicitamente mesmo com Python otimizado; o checkout da CI não persiste credenciais.

## 0.1.0-preview.5

- Surrogates UTF-16 sem par em strings/chaves são recusados antes de reparação JSON ou conversão de URI, incluindo seletores livres, query, corpo, extensões, arrays, unions e JSON bruto. U+FFFD literal, pares astrais, Unicode não ASCII e números exatos permanecem preservados.
- Encoder estrito no writer efetivo e verificação do único snapshot serializado mantêm getters/converters em uma invocação, sem reflection prewalk. Converters com WriteStringValue/WritePropertyName, WriteRawValue e escritas segmentadas recebem controles próprios.
- Credenciais/tokens e endpoints inválidos falham cedo com mensagem fixa; JSON de resposta Unicode inválido falha como protocolo antes de projeção. Generated DTOs/métodos/contratos não mudam.
- Preserva as correções de privacidade, framing, arquivos, DNS/TCP/TLS pertencentes à requisição, Tasks canceladas nativas, import waiter e continuações da preview.4, incluindo os limites explícitos de credenciais curtas e CN fallback.

## 0.1.0-preview.4

- Une intervalos de redaction sobrepostos de credenciais, UUIDs e capabilities; preserva nomes declarados, booleanos e null, protegendo valores fiscais/extensões do mesmo snapshot serializado.
- Observa Location em 1xx, resposta final e trailers, incluindo componentes codificados/query/fragment/matrix para privacidade. Observação incompleta/recusada retira recuperação por seletor. CL+TE é recusado.
- Seleciona respostas por status e MIME declarados. PDF/XML/ZIP/XLSX recebem reconhecimento estrutural limitado; `FileReadiness` distingue proteção/unsupported de prontidão.
- Adiciona `WaitMonitorImportAsync`, estados terminais inválidos com `erro`, e três leitores `ReadMonitor*Async` com limite nativo de itens e continuação tipada em memória.
- DNS/TCP/TLS têm ownership de requisição e cleanup antes da conclusão. DNS usa helper compilado/embutido com stdin. Cancelamento explícito passa a Task cancelada com `WebmaniaCancellationException` e metadados privados tipados. Esta mudança requer capturar OperationCanceledException em vez de apenas WebmaniaException para cancelamento do chamador.
- Mantém 13 módulos, 102 métodos de operação (101 habilitados), 3.038 tipos nominais gerados, contratos primários e semântica de Optional/JsonNumber. Credenciais curtas continuam substring literais; CN fallback do runtime continua documentado, sem alegar strict SAN-only.

Candidato local; não publicado. Validação fiscal com a API real e demais plataformas permanecem pendentes.
