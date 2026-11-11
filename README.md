<p align="center"><img src=".github/assets/webmania-logo.png" alt="Webmania" width="320"></p>

# Webmania SDK para .NET (C#)

Através do emissor fiscal da Webmania®, você conta com a emissão e o arquivamento dos seus documentos fiscais, cálculo automático de impostos, geração do DANFE para impressão e envio automático de e-mails para os clientes. Com este SDK, o seu sistema em .NET (C#) se integra à nossa REST API para emitir, consultar e cancelar NF-e, NFC-e, NFS-e, CT-e, MDF-e, NFCom e DC-e, baixar XML e PDF dos documentos e usar o Monitor Fiscal.

- Emissor de Nota Fiscal Webmania®: [Saiba mais](https://webmania.com.br/nota-fiscal-eletronica/)
- Documentação da REST API: [Visualizar](https://webmania.com.br/docs/)
- Índice da documentação para assistentes de IA: [llms.txt](https://webmania.com.br/docs/llms.txt)

## Requisitos

- Contrate um dos planos de Nota Fiscal Eletrônica da Webmania®: [Assine agora mesmo](https://webmania.com.br/nota-fiscal-eletronica/#plans-section).
- .NET 10 (SDK 10.0.403 ou superior). O pacote pode ser usado em C#, F# e Visual Basic.
- Credenciais de acesso da sua empresa, disponíveis no [Painel de Controle](https://webmania.com.br/painel/).

## Instalação

O SDK é distribuído pelo repositório [github.com/webmaniabr/webmania-dotnet](https://github.com/webmaniabr/webmania-dotnet) e ainda não está publicado no nuget.org. Clone o repositório, gere o pacote em uma pasta de feed local e instale-o no projeto da aplicação:

```sh
git clone https://github.com/webmaniabr/webmania-dotnet.git
dotnet pack webmania-dotnet/src/Webmania.Sdk -c Release -o /caminho/feed-local
dotnet add package Webmania.Sdk --version 0.1.0-preview.6 --source /caminho/feed-local
```

## Credenciais

Cada serviço usa a sua própria credencial. Guarde-as em variáveis de ambiente (ou em um cofre de segredos) no servidor e nunca as escreva no código-fonte:

| Variável de ambiente | Uso |
|---|---|
| `WEBMANIA_CONSUMER_KEY`, `WEBMANIA_CONSUMER_SECRET`, `WEBMANIA_ACCESS_TOKEN`, `WEBMANIA_ACCESS_TOKEN_SECRET` | NF-e e NFC-e (`WebmaniaCredentials.Company`) e arquivos da NF-e (`WebmaniaCredentials.Api1FilesCompany`) |
| `WEBMANIA_ISSUER_TOKEN` | NFS-e, CT-e, MDF-e, NFCom e DC-e (`WebmaniaCredentials.Issuer`) e seus arquivos (`WebmaniaCredentials.Api2FilesCompanyBearer`) |
| `WEBMANIA_MONITOR_TOKEN` | Monitor Fiscal (`WebmaniaCredentials.Monitor`) |

```csharp
using System.Text.Json;
using Webmania;

string Env(string nome) => Environment.GetEnvironmentVariable(nome) ?? throw new InvalidOperationException($"Configure {nome}.");

using var nfe = new WebmaniaClient(WebmaniaCredentials.Company(              // NF-e e NFC-e
    Env("WEBMANIA_CONSUMER_KEY"), Env("WEBMANIA_CONSUMER_SECRET"),
    Env("WEBMANIA_ACCESS_TOKEN"), Env("WEBMANIA_ACCESS_TOKEN_SECRET")));
using var emissor = new WebmaniaClient(WebmaniaCredentials.Issuer(Env("WEBMANIA_ISSUER_TOKEN")));  // NFS-e, CT-e, MDF-e, NFCom e DC-e

// Monta os parâmetros tipados de uma operação a partir de um objeto.
T Parametros<T>(object valor) where T : class =>
    WebmaniaJson.FromElement<T>(JsonSerializer.SerializeToElement(valor)) ?? throw new InvalidOperationException();
```

Cada `WebmaniaClient` atende o serviço da credencial que recebeu. Os métodos são assíncronos (`...Async`, com `CancellationToken` opcional) e devolvem `ApiResponse`: `StatusCode` traz o status HTTP e `Json` o JSON da resposta.

## Utilização

Os exemplos abaixo usam o ambiente de **homologação** (`ambiente` 2), em que os documentos não têm validade fiscal. Para emitir em produção, envie `ambiente` 1. Os pedidos são montados a partir de JSON; informe valores decimais como texto (`"44.90"`) para manter a precisão.

### Status do SEFAZ e certificado A1

```csharp
var sefaz = await nfe.NfeGetSefazStatusAsync(new NfeGetSefazStatusParameters());
Console.WriteLine(sefaz.Json.GetProperty("status").GetString());          // online ou offline

var certificado = await nfe.NfeGetCertificateValidityAsync(new NfeGetCertificateValidityParameters());
Console.WriteLine(certificado.Json.GetProperty("expiration").GetInt32());  // dias até o vencimento do certificado A1
```

### Emitir NF-e

Para emitir uma NF-e, use o método `NfeIssueDocumentAsync` com o JSON da nota no campo `body`. O mesmo método emite NFC-e com `"modelo": 2`.

```csharp
var dados = JsonDocument.Parse("""
    {
      "operacao": 1,
      "natureza_operacao": "Venda de produção do estabelecimento",
      "modelo": 1,
      "finalidade": 1,
      "ambiente": 2,
      "cliente": {
        "cpf": "000.000.000-00", "nome_completo": "Nome do Cliente",
        "endereco": "Av. Brg. Faria Lima", "numero": "1000", "bairro": "Itaim Bibi",
        "cidade": "São Paulo", "uf": "SP", "cep": "00000-000", "email": "nome@email.com"
      },
      "produtos": [{
        "nome": "Nome do produto", "codigo": "nome-do-produto", "ncm": "6109.10.00",
        "quantidade": "1", "unidade": "UN", "origem": 0,
        "subtotal": "44.90", "total": "44.90", "classe_imposto": "REF1000"
      }],
      "pedido": {"presenca": 2, "modalidade_frete": 9, "pagamento": 0, "forma_pagamento": "01", "total": "44.90"}
    }
    """).RootElement;

var emissao = await nfe.NfeIssueDocumentAsync(Parametros<NfeIssueDocumentParameters>(new { body = dados }));
var nota = emissao.Json;
var uuid = nota.GetProperty("uuid").GetString()!;                        // identificador único da nota fiscal
Console.WriteLine(nota.GetProperty("status").GetString());               // aprovado, reprovado, processamento...
if (nota.TryGetProperty("chave", out var chave)) Console.WriteLine(chave);       // chave de acesso
if (nota.TryGetProperty("xml", out var xmlUrl)) Console.WriteLine(xmlUrl);       // URL do XML
if (nota.TryGetProperty("danfe", out var danfeUrl)) Console.WriteLine(danfeUrl); // URL do DANFE (PDF)
if (nota.TryGetProperty("motivo", out var motivo)) Console.WriteLine(motivo);    // retorno do SEFAZ
```

Somente o status `aprovado` indica uma nota autorizada. Quando o retorno for `processamento`, consulte a nota alguns segundos depois.

### Consultar NF-e

```csharp
var consulta = await nfe.NfeGetDocumentAsync(Parametros<NfeGetDocumentParameters>(new { uuid }));
Console.WriteLine(consulta.Json.GetProperty("status").GetString());
```

A consulta também aceita a chave de acesso (`chave`).

### Cancelar NF-e

```csharp
var cancelamento = await nfe.NfeCancelDocumentAsync(Parametros<NfeCancelDocumentParameters>(
    new { body = new { uuid, motivo = "Cancelamento por erro no pedido." } }));   // motivo: mínimo de 15 caracteres
Console.WriteLine(cancelamento.Json.GetProperty("status").GetString());
```

### Baixar o DANFE e o XML

Os arquivos da NF-e usam um cliente com a credencial de arquivos da empresa:

```csharp
using var arquivos = new WebmaniaClient(WebmaniaCredentials.Api1FilesCompany(
    Env("WEBMANIA_CONSUMER_KEY"), Env("WEBMANIA_CONSUMER_SECRET"),
    Env("WEBMANIA_ACCESS_TOKEN"), Env("WEBMANIA_ACCESS_TOKEN_SECRET")));

var danfe = await arquivos.NfeDownloadDanfeAsync(Parametros<NfeDownloadDanfeParameters>(new { identifier = uuid }));
if (danfe.IsFile) await File.WriteAllBytesAsync("danfe.pdf", danfe.Bytes.ToArray());

var xml = await arquivos.NfeDownloadXmlAsync(Parametros<NfeDownloadXmlParameters>(new { identifier = uuid }));
if (xml.IsFile) await File.WriteAllBytesAsync("nfe.xml", xml.Bytes.ToArray());
```

Confira `IsFile` antes de gravar: a resposta pode indicar que o arquivo ainda está sendo gerado.

### Emitir, consultar e cancelar NFS-e

A NFS-e é emitida a partir de um ou mais RPS com o método `EmitterIssueNfseAsync`:

```csharp
var rps = JsonDocument.Parse("""
    {
      "ambiente": 2,
      "rps": [{
        "servico": {
          "valor_servicos": "15.00",
          "discriminacao": "Prestação de serviço referente ao mês 10/2026",
          "classe_imposto": "REF000000"
        },
        "tomador": {"cpf": "000.000.000-00", "nome_completo": "Nome do tomador"}
      }]
    }
    """).RootElement;
var nfse = await emissor.EmitterIssueNfseAsync(Parametros<EmitterIssueNfseParameters>(new { body = rps }));
var uuidNfse = nfse.Json.GetProperty("uuid").GetString()!;
Console.WriteLine(nfse.Json.GetProperty("status").GetString());

var consultaNfse = await emissor.EmitterGetNfseAsync(Parametros<EmitterGetNfseParameters>(new { identifier = uuidNfse }));
Console.WriteLine(consultaNfse.Json.GetProperty("status").GetString());

var canceladaNfse = await emissor.EmitterCancelNfseAsync(Parametros<EmitterCancelNfseParameters>(new
{
    body = new
    {
        uuid = uuidNfse,
        motivo = "1",                                              // código do motivo (veja a documentação da NFS-e)
        justificativa = "Serviço cancelado a pedido do tomador."  // mínimo de 15 caracteres
    }
}));
Console.WriteLine(canceladaNfse.Json.GetProperty("status").GetString());
```

### Tratamento de erros

Toda falha lança `WebmaniaException`:

```csharp
try
{
    await nfe.NfeIssueDocumentAsync(Parametros<NfeIssueDocumentParameters>(new { body = dados }));
}
catch (WebmaniaException erro)
{
    Console.Error.WriteLine(erro.Kind);        // validation (dados recusados antes do envio), business (recusa da API ou do SEFAZ), http, timeout...
    Console.Error.WriteLine(erro.StatusCode);  // status HTTP, quando houver
    Console.Error.WriteLine(erro.Message);     // mensagem, com dados sensíveis mascarados
    if (erro.OutcomeUncertain)
    {
        // A requisição pode ter sido processada: consulte a nota antes de emitir de novo.
    }
}
```

O SDK não repete requisições automaticamente. Se uma emissão ou um cancelamento terminar com resultado desconhecido (`OutcomeUncertain` verdadeiro), consulte o documento antes de tentar novamente, para não emitir em duplicidade.

## Métodos disponíveis

**NF-e e NFC-e** — `WebmaniaClient` com `WebmaniaCredentials.Company`

- **NfeIssueDocumentAsync**: Emissão de NF-e ou NFC-e junto ao SEFAZ.
- **NfeGetDocumentAsync**: Consulta da nota fiscal pelo UUID, pela chave de acesso ou pelo ID do pedido.
- **NfeCancelDocumentAsync**: Cancelamento de NF-e ou NFC-e.
- **NfeCreateCorrectionAsync**: Carta de correção.
- **NfeVoidNumberingAsync**: Inutilização de sequência de numeração.
- **NfeIssueReturnAsync**: Emissão de NF-e de devolução.
- **NfeIssueComplementAsync**: Emissão de NF-e complementar.
- **NfeIssueAdjustmentAsync**: Emissão de NF-e de ajuste.
- **NfeCreateManifestationAsync**: Manifestação do destinatário.
- **NfeCreatePaymentEventAsync** / **NfeCancelPaymentEventAsync**: Evento de conciliação financeira (ECONF) e o seu cancelamento.
- **NfeCreateIbsCbsEventAsync** / **NfeCancelIbsCbsEventAsync**: Eventos de IBS/CBS e o seu cancelamento.
- **NfeListTaxClassesAsync** / **NfeSaveTaxClassesAsync** / **NfeDeleteTaxClassesAsync**: Classes de imposto.
- **FiscalCreateOrPollReportAsync**: Relatórios fiscais.
- **FiscalUpdateCompanyAsync**: Atualização dos dados da empresa emissora.
- **NfeGetSefazStatusAsync**: Verifica se o SEFAZ está online ou offline.
- **NfeGetCertificateValidityAsync**: Verifica quantos dias faltam para o certificado A1 expirar.

**NFS-e** — `WebmaniaClient` com `WebmaniaCredentials.Issuer`

- **EmitterIssueNfseAsync**: Emissão de NFS-e a partir de RPS.
- **EmitterGetNfseAsync**: Consulta da NFS-e.
- **EmitterCancelNfseAsync**: Cancelamento da NFS-e.
- **EmitterReplaceNfseAsync**: Substituição de NFS-e.
- **EmitterManifestNfseAsync**: Manifestação sobre NFS-e nacional.
- **EmitterGetNfseProviderStatusAsync**: Recursos do provedor de NFS-e do município da empresa.

**CT-e e CT-e OS** — `WebmaniaClient` com `WebmaniaCredentials.Issuer`

- **EmitterIssueCteAsync** / **EmitterIssueSimplifiedCteAsync**: Emissão de CT-e, CT-e OS e CT-e simplificado.
- **EmitterGetCteAsync**: Consulta do CT-e.
- **EmitterCancelCteAsync**: Cancelamento do CT-e.
- **EmitterCorrectCteAsync**: Carta de correção do CT-e.
- **EmitterConfirmCteDeliveryAsync** / **EmitterCancelCteDeliveryAsync**: Comprovante de entrega e o seu cancelamento.
- **EmitterLinkCtePaymentAsync** / **EmitterCancelCtePaymentLinkAsync** / **EmitterGetCtePaymentEventsAsync**: Eventos de pagamento do CT-e.

**MDF-e** — `WebmaniaClient` com `WebmaniaCredentials.Issuer`

- **EmitterIssueMdfeAsync**: Emissão do MDF-e.
- **EmitterGetMdfeAsync**: Consulta do MDF-e pela chave de acesso.
- **EmitterCloseMdfeAsync**: Encerramento do MDF-e.
- **EmitterCancelMdfeAsync**: Cancelamento do MDF-e.
- **EmitterAddMdfeConductorAsync**: Inclusão de condutor.

**NFCom e DC-e** — `WebmaniaClient` com `WebmaniaCredentials.Issuer`

- **EmitterIssueNfcomAsync** / **EmitterGetNfcomAsync** / **EmitterCancelNfcomAsync**: Emissão, consulta e cancelamento da NFCom.
- **EmitterGetNfcomPaymentEventsAsync**: Eventos de pagamento da NFCom.
- **EmitterGetNfcomServiceStatusAsync**: Disponibilidade do serviço da NFCom no SEFAZ.
- **EmitterIssueDceAsync** / **EmitterGetDceAsync** / **EmitterCancelDceAsync**: Emissão, consulta e cancelamento da DC-e.

**Arquivos (XML e PDF)** — `WebmaniaClient` com `WebmaniaCredentials.Api1FilesCompany` (NF-e) ou `WebmaniaCredentials.Api2FilesCompanyBearer` (demais documentos)

- **NfeDownloadDanfeAsync** / **NfeDownloadSimpleDanfeAsync** / **NfeDownloadLabelDanfeAsync**: DANFE completo, simplificado e etiqueta.
- **NfeDownloadXmlAsync** / **NfeDownloadCancellationXmlAsync**: XML da NF-e e do cancelamento.
- **NfeDownloadCorrectionPdfAsync** / **NfeDownloadCorrectionXmlAsync**: PDF e XML da carta de correção.
- **FileGetNfseXmlAsync** / **FileGetNfsePdfAsync** / **FileGetNfseRpsPdfAsync**: XML e PDF da NFS-e e PDF do RPS.
- **FileGetCteXmlAsync** / **FileGetCtePdfAsync** / **FileGetMdfeXmlAsync** / **FileGetMdfePdfAsync** / **FileGetNfcomXmlAsync** / **FileGetNfcomPdfAsync** / **FileGetDceXmlAsync** / **FileGetDcePdfAsync**: XML e PDF de CT-e, MDF-e, NFCom e DC-e.

**Monitor Fiscal** — `WebmaniaClient` com `WebmaniaCredentials.Monitor`

- **MonitorSearchDocumentsAsync** / **MonitorGetDocumentAsync** / **MonitorListDocumentEventsAsync**: Busca e consulta de documentos recebidos e do histórico de eventos.
- **MonitorManifestDocumentAsync**: Manifestação do destinatário.
- **MonitorImportDocumentAsync** / **MonitorGetImportStatusAsync**: Importação por chave de acesso, XML ou ZIP.
- **MonitorListCompaniesAsync** / **MonitorCreateCompanyAsync** / **MonitorUpdateCompanyAsync**: Empresas monitoradas.

**Gerencial B2B** — `WebmaniaClient` com `WebmaniaCredentials.Reseller`

- **B2bV1CreateCompaniesAsync** / **B2bV1ListCompaniesAsync** / **B2bV1GetProcessedRequestsAsync**: Cadastro de empresas, listagem e contagem de notas processadas por empresa.

A lista completa, com todos os métodos do Monitor Fiscal e dos arquivos, está em [docs/OPERATIONS.md](docs/OPERATIONS.md).

## Exemplo completo em homologação

A pasta [examples/homologacao](examples/homologacao) traz um programa que emite, consulta, baixa os arquivos e cancela uma NF-e e uma NFS-e em homologação, usando as credenciais das variáveis de ambiente. Por segurança, ele só envia requisições quando `WEBMANIA_LIVE=1` está definido e `WEBMANIA_LIVE_ALLOWED_HOSTS` lista os hosts da API. Para rodar só as consultas (status do SEFAZ, certificado e provedor de NFS-e):

```sh
WEBMANIA_LIVE=1 WEBMANIA_LIVE_SCOPE=readonly WEBMANIA_LIVE_ALLOWED_HOSTS=webmania.com.br,nfe.webmania.com.br,api.webmania.com.br sh examples/homologacao/run.sh
```

As variáveis aceitas e o formato da saída estão em [examples/homologacao/README.md](examples/homologacao/README.md).

## Documentação

- Documentação da REST API: [NF-e / NFC-e](https://webmania.com.br/docs/rest-api-nfe/) · [NFS-e](https://webmania.com.br/docs/rest-api-nfse/) · [CT-e](https://webmania.com.br/docs/rest-api-cte/) · [MDF-e](https://webmania.com.br/docs/rest-api-mdfe/) · [NFCom](https://webmania.com.br/docs/rest-api-nfcom/) · [DC-e](https://webmania.com.br/docs/rest-api-dce/) · [Monitor Fiscal](https://webmania.com.br/docs/rest-api-monitor-fiscal/) · [Gerencial B2B](https://webmania.com.br/docs/rest-api-gerencial-b2b/)
- Contratos OpenAPI: [API 2.0](https://api.webmania.com.br/docs/openapi) e [NF-e](https://webmania.com.br/docs/rest-api-nfe/?format=openapi)
- Detalhes técnicos do SDK (URL base, arquivos, relatórios, limites): [docs/INTEGRATION.md](docs/INTEGRATION.md)
- Lista completa de métodos: [docs/OPERATIONS.md](docs/OPERATIONS.md)
- Desenvolvimento e testes do SDK: [docs/TESTING.md](docs/TESTING.md)
- Histórico de versões: [CHANGELOG.md](CHANGELOG.md)

## Suporte

Qualquer dúvida entre em contato na nossa [Central de Ajuda](https://ajuda.webmania.com.br) ou acesse o [Painel de Controle](https://webmania.com.br/painel/) para conversar em tempo real no Chat ou Abrir um chamado.

## Licença

Licença proprietária Webmania®: veja [LICENSE](LICENSE) e os [Termos de Uso](https://webmania.com.br/termos-de-uso/). As dependências de terceiros mantêm as suas próprias licenças.
