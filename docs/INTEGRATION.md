# Detalhes técnicos de integração

Este documento reúne os detalhes técnicos do SDK para .NET (C#) que complementam o [README](../README.md): autenticação, URL base, limites de transporte, arquivos, relatórios e o tratamento do resultado fiscal. O texto é o mesmo que estava no README até esta versão.

## Escopo e disponibilidade

Integração de **Emissor Fiscal e Monitor Fiscal** em aplicações de servidor. O Emissor abrange NF-e/NFC-e na API1 e NFS-e, CT-e/CT-e OS, MDF-e, NFCom e DC-e na API2, com arquivos, eventos e as três operações gerenciais B2B públicas da API1. O Monitor mantém apenas as famílias de documentos e capacidades do seu contrato documentado.

Disponível no repositório público [github.com/webmaniabr/webmania-dotnet](https://github.com/webmaniabr/webmania-dotnet), com instalação a partir do clone (veja a seção Instalação). Ainda não há publicação em registro de pacotes; os nomes e versões abaixo identificam os pacotes gerados a partir desta fonte. Regras do provedor, aceitação fiscal real e plataformas adicionais permanecem com limitações explícitas.

## Visão geral para desenvolvedores

| Item | Detalhe |
|---|---|
| Status | Disponível no GitHub: versão `0.1.0-preview.6` (pacote NuGet `Webmania.Sdk`), instalada a partir de um feed local gerado pelo clone do repositório; ainda sem publicação no nuget.org |
| Runtime | .NET 10 (SDK fixado em [global.json](../global.json): `10.0.403`); consumível de C#, F# e Visual Basic |
| Cliente | `Webmania.WebmaniaClient(WebmaniaCredentials, WebmaniaClientOptions)` — o escopo da credencial define o serviço (NF-e/NFC-e API 1.0, gerencial B2B, emissor API 2.0, Monitor Fiscal e arquivos); métodos assíncronos com `CancellationToken` |
| Erros | `WebmaniaException`, com `Kind`, `StatusCode` e `OutcomeUncertain` |
| Contrato | OpenAPI fixado em [contracts.lock.json](../contracts.lock.json) (`contract_digest` `371a499f74012bb6b41f23642869dee4954e8394dcba7642edc701aedb3dd59e`) |

### Autenticação

Crie a credencial com a fábrica `WebmaniaCredentials`:

- `Company(consumerKey, consumerSecret, accessToken, accessTokenSecret)`, `Reseller(...)` e `Api1FilesCompany(...)` — API 1.0, enviadas nos cabeçalhos `X-Consumer-Key`, `X-Consumer-Secret`, `X-Access-Token` e `X-Access-Token-Secret`.
- `Issuer(bearer)`, `Monitor(bearer)` e `Api2FilesCompanyBearer(bearer)` — API 2.0, enviadas como `Authorization: Bearer <token>`.
- `Api1FilesDocumentToken`, `Api1ReportCapability`, `Api2FilesDocumentToken` e `Api2FilesAnonymous` — modos explícitos de acesso a arquivos.

O exemplo abaixo lê o token com `Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")`. Consumidores completos em [examples/CSharp](../examples/CSharp), [examples/FSharp](../examples/FSharp) e [examples/VisualBasic](../examples/VisualBasic).

## Requisitos

.NET 10 para C#, F# e Visual Basic. Perfil atual: Linux arm64/Ubuntu 24.04.5, SDK 10.0.403/runtime 10.0.14. Windows e macOS não estão qualificados para este pacote. O helper DNS incorporado, já compilado, usa o host `dotnet` do runtime; sem ele, configure `WebmaniaClientOptions.DnsHelperHost`.

## Instalação

Clone o repositório público, gere o pacote em uma pasta de feed local e instale-o no projeto da aplicação:

```sh
git clone https://github.com/webmaniabr/webmania-dotnet.git
dotnet pack webmania-dotnet/src/Webmania.Sdk -c Release -o /caminho/feed-local
dotnet add package Webmania.Sdk --version 0.1.0-preview.6 --source /caminho/feed-local
```

O pacote gerado contém o helper DNS compilado; não exige gerador.

## Primeira consulta

Configure somente a credencial do serviço escolhido, por instância, a partir de ambiente privado ou cofre de segredos. O exemplo não emite nem cancela documentos. Execute consultas apenas com conta e ambiente autorizados; uma origem de fixture controlada deve ser configurada explicitamente pela API do cliente.

```csharp
using Webmania;
var token = Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")
    ?? throw new InvalidOperationException("Configure WEBMANIA_MONITOR_TOKEN.");
var options = new WebmaniaClientOptions();
if (Environment.GetEnvironmentVariable("WEBMANIA_LOOPBACK_TEST_URL") is { Length: > 0 } local)
    options.LoopbackTestEndpoint = new Uri(local);
using var client = new WebmaniaClient(WebmaniaCredentials.Monitor(token), options);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
try
{
    var result = await client.MonitorListCompaniesAsync(new MonitorListCompaniesParameters
    {
        Page = new JsonNumber("1"), PerPage = new JsonNumber("10")
    }, cancellation.Token);
    Console.WriteLine(result.StatusCode);
}
catch (WebmaniaException error) { Console.Error.WriteLine(error.Kind); }
```

A construção do cliente não comprova habilitação da conta. Respostas de sucesso e conversões explícitas para JSON podem conter dados fiscais, credenciais novas ou URLs privadas. Exponha apenas os campos necessários à aplicação.

Os projetos completos em examples/FSharp e examples/VisualBasic usam o mesmo pacote e consulta. As formas idiomáticas correspondentes são:

```fsharp
open System
open Webmania
let run () = task {
    let options = WebmaniaClientOptions()
    let local = Environment.GetEnvironmentVariable("WEBMANIA_LOOPBACK_TEST_URL")
    if not (String.IsNullOrEmpty(local)) then options.LoopbackTestEndpoint <- Uri(local)
    use client = new WebmaniaClient(
        WebmaniaCredentials.Monitor(Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")),
        options)
    let parameters = MonitorListCompaniesParameters()
    parameters.PerPage <- Optional<JsonNumber>.Of(JsonNumber("10"))
    let! response = client.MonitorListCompaniesAsync(parameters)
    printfn "%d" response.StatusCode
}
run().GetAwaiter().GetResult()
```

```vbnet
Imports Webmania
' Dentro de um método Async da aplicação:
Dim options = New WebmaniaClientOptions()
Dim local = Environment.GetEnvironmentVariable("WEBMANIA_LOOPBACK_TEST_URL")
If Not String.IsNullOrEmpty(local) Then options.LoopbackTestEndpoint = New Uri(local)
Using client = New WebmaniaClient(
    WebmaniaCredentials.Monitor(Environment.GetEnvironmentVariable("WEBMANIA_MONITOR_TOKEN")),
    options)
    Dim parameters = New MonitorListCompaniesParameters With {
        .PerPage = [Optional](Of JsonNumber).FromValue(New JsonNumber("10"))
    }
    Dim response = Await client.MonitorListCompaniesAsync(parameters)
    Console.WriteLine(response.StatusCode)
End Using
```

## Integração e limites

`Optional<T>` distingue omissão (`default`), `Optional<T>.Null` e valor, inclusive zero e false. DTOs usam propriedades anotadas com os nomes exatos do JSON e `Extras` para campos adicionais; campos extras não podem sombrear propriedades declaradas. Unions têm fábricas `FromVariantN` e preservam o JSON recebido. `JsonNumber` recebe texto numérico exato (até 4.096 caracteres para a validação numérica); números decimais enviados como texto permanecem texto. Respostas preservam valores futuros de enums e campos desconhecidos. A presença de `error` indica falha mesmo com valor vazio, `false` ou `null`. `WebmaniaJson.ToElement` e `ApiResponse.Json` são acessos explícitos a dados sensíveis; guarde-os com o cuidado dos próprios documentos. Objetos `ToString()` não exibem conteúdo fiscal ou credenciais.

Por padrão, o cliente usa as bases HTTPS do contrato. Para um ambiente autorizado, configure explicitamente `WebmaniaClientOptions.ServiceEndpoint`, por exemplo `new Uri("https://ambiente.example.invalid/prefixo/")`; sua origem e prefixo recebem as rotas fixadas, preservando o escopo da credencial desta instância. Userinfo, query, fragmento, segmentos de ponto e separadores escapados são rejeitados. Nos três trechos acima, `WEBMANIA_LOOPBACK_TEST_URL` é uma configuração opcional e exclusiva para uma fixture local controlada; deixe-a ausente para usar o serviço padrão. Para um endpoint HTTPS de serviço autorizado, configure `ServiceEndpoint` explicitamente. Uma URL retornada pela API nunca configura o endpoint automaticamente. `LoopbackTestEndpoint` é separado e limitado a localhost para ensaios HTTP/TLS; `LoopbackTestRootCertificate` só funciona com HTTPS local e mantém a verificação de hostname. A política de hostname preserva a verificação do SslStream/.NET, inclusive fallback para CN em certificado confiável sem SAN; este pacote não declara política estrita SAN-only. TLS exige autenticação e criptografia, versões 1.2/1.3, e recusa suites anônimas ou de criptografia NULL.

Cancelamento explícito pelo `CancellationToken` agora produz Task `Canceled` e `WebmaniaCancellationException : OperationCanceledException`. A propriedade `Diagnostic` preserva `WebmaniaException` tipada com `OperationId`, `Kind=cancelled`, status, `OutcomeUncertain`, referência privada, detalhes e orientação de recuperação; as mesmas propriedades ficam disponíveis diretamente. `ToString()` e JSON ordinário ficam redigidos. Capture `OperationCanceledException` para cancelamento nativo, ou `WebmaniaCancellationException` para a recuperação tipada. Prazos internos permanecem `WebmaniaException` com `Kind=timeout`. Uma mutação cujo write HTTP já pode ter ocorrido conserva a incerteza e nunca é reenviada automaticamente.

`ReadMonitorCompaniesAsync`, `ReadMonitorCompanyMunicipalitiesAsync` e `ReadMonitorReportsAsync` retornam `MonitorBatch<T>` tipado com `maximumItems` (1 a 1.000.000) e `maximumPages` (1 a 10.000). O `MonitorContinuation<T>` em memória se vincula ao snapshot serializado original, incluindo filtros, página inicial e `per_page`; entradas alteradas são recusadas antes da rede. Ele expõe somente a posição (`Page` exata e `ItemIndex`) como propriedades deliberadas; JSON/ToString ordinário não exportam o snapshot. Uma parada em 75 itens com páginas de 50 produz página 2/índice 25. O restante dessa página fica retido e é entregue sem nova consulta; não há pré-fetch nem I/O para descobrir uma página a mais. Passe o mesmo conjunto de parâmetros e a continuação para retomar. Continuações são imutáveis e podem ser reutilizadas deliberadamente para reler a mesma posição; não representam persistência durável nem consistência fiscal de um conjunto remoto que mude entre páginas.

`WaitMonitorImportAsync` consulta somente um UUID de importação existente. `concluido` e `invalido` são resultados terminais tipados, preservando o `erro` declarado; `error`/`errors`, `success=false` ou formas fora do schema de sucesso continuam sendo falhas. Valores futuros de status são preservados pelo método direto, mas o waiter os recusa como estado desconhecido. O waiter de importação e `PollFiscalReportAsync` congelam os parâmetros uma vez e usam `PollOptions` finitas, relógio monotônico, intervalo mínimo de cinco segundos e cancelamento durante espera/rede. Não há repetição de erro de API nem download/follow de uma URL recebida.

Arquivos expõem `Representation`, `Readiness` (`Ready`, `Protected`, `Unsupported`, `NotFile`) e bytes. PDF usa o subconjunto de xref clássico; perfis incrementais/híbridos e ZIP64 não recebem prontidão. PDF/ZIP criptografados preservam bytes como Protected. Verifique `OutcomeUncertain` e acesse `SafeReference?.Reveal()` somente deliberadamente.

## Ambiente, URL base e relatórios

`WebmaniaClientOptions.ServiceEndpoint` substitui a base HTTPS do contrato por uma base explícita, com prefixo de caminho opcional, sem usuário, query, fragmento ou segmentos `.`/`..`; `LoopbackTestEndpoint` aceita apenas a origem de uma fixture local. O exemplo de homologação lê `WEBMANIA_API1_BASE_URL` (padrão `https://webmania.com.br/api`, credencial `Company`), `WEBMANIA_FILES_BASE_URL` (padrão `https://nfe.webmania.com.br`, `Api1FilesCompany`) e `WEBMANIA_API2_BASE_URL` (padrão `https://api.webmania.com.br`, `Issuer` e `Api2FilesCompanyBearer`); o SDK não lê ambiente por conta própria. Quando o processo não tem um host `dotnet` ao lado do runtime (por exemplo, publicação self-contained), configure `WebmaniaClientOptions.DnsHelperHost` com o caminho absoluto de um host .NET para o resolvedor de nomes.

`FiscalDownloadReportAsync` atende relatórios legados: CSV e XLSX rotulados `text/xml` são identificados pelos bytes (assinatura ZIP = XLSX; qualquer outro corpo = CSV devolvido byte a byte, inclusive Latin-1) e `MediaType` informa o formato detectado; XML, HTML ou JSON nesse download é recusado e nenhuma outra operação ganha essa tolerância. Relatórios atuais usam `FiscalDownloadReportV2Async` com `WebmaniaCredentials.ReportCapability()` na base da API 2.0; um JSON com `error`, mesmo em HTTP 200, é falha.

## Credenciais e resultado fiscal

Credenciais da empresa API1, revenda API1, Bearer do emissor API2 e Bearer Monitor pertencem a escopos diferentes. Arquivos exigem a modalidade de acesso documentada. Uma falha não autoriza trocar automaticamente o modo ou a credencial. Mantenha segredos no servidor.

O catálogo contém 102 operações habilitadas. `nfeDownloadIbsCbsEventXml` não está disponível nesta versão do SDK: a chamada falha com `Kind` igual a `capability_unavailable` e nenhuma requisição é enviada. Condições fiscais, municipais, de cadastro e de provedor continuam sujeitas ao contrato e ao serviço. Validação local, cálculo ou prévia não comprovam autorização fiscal.

Não há retry automático de operação nem seguimento automático de redirects. Depois de possível envio de uma operação com efeito, uma falha pode indicar resultado desconhecido. Preserve o seletor original com segurança, consulte a tentativa anterior e decida explicitamente como prosseguir. Uma referência protegida exige acesso deliberado e pode estar ausente; não é uma chave de idempotência.

## Arquivos e processamento

Verifique tipo de resultado, status e representação antes de consumir bytes. JSON pendente, HTML e redirect não comprovam PDF pronto. Proteção de PDF/ZIP é distinta da autorização de download; não há descriptografia automática nem confirmação de autenticidade fiscal. O reconhecimento de formatos cobre somente o perfil documentado desta implementação. Uma Location revelada continua privada e exige decisão explícita de origem e autenticação para outra transferência.

Paginação e espera são optativas e limitadas quando disponíveis. Um estado futuro ou desconhecido não significa conclusão. Não repita emissão, cancelamento ou importação inicial como se fossem consultas de processamento.

Para migrar uma integração anterior, remapeie métodos, modelos e escopos de credencial pela referência desta versão. Não há promessa de substituição automática de outro SDK; compare também o changelog quando disponível.

## Documentação para assistentes e contratos

Índices em Markdown para assistentes: [documentação Webmania](https://webmania.com.br/docs/llms.txt) e [API 2.0](https://api.webmania.com.br/llms.txt). Contratos OpenAPI: [API 2.0](https://api.webmania.com.br/docs/openapi) e [NF-e](https://webmania.com.br/docs/rest-api-nfe/?format=openapi).

No catálogo de SDKs, `dotnet` aparece como disponível no repositório público, com instalação pelo clone; plataforma .NET 10. Estar listado nesses índices não amplia a cobertura documentada deste SDK.
