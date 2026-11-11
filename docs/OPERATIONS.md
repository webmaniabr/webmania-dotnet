# Operações

Todas as operações do contrato público que o SDK para .NET (C#) expõe, com o cliente e o método correspondentes. As condições fiscais de cada operação estão na documentação da REST API em https://webmania.com.br/docs/.

Cada método recebe a classe `<Operação>Parameters` correspondente e um `CancellationToken` opcional.

| Operação | Cliente | Método | HTTP | Estado |
|---|---|---|---|---|
| `nfeGetCertificateValidity` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeGetCertificateValidityAsync` | `GET /1/nfe/certificado/` | habilitada |
| `nfeGetSefazStatus` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeGetSefazStatusAsync` | `GET /1/nfe/sefaz/` | habilitada |
| `nfeGetDocument` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeGetDocumentAsync` | `GET /1/nfe/consulta/` | habilitada |
| `nfeQueryDocument` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeQueryDocumentAsync` | `POST /1/nfe/consulta/` | habilitada |
| `nfeCreateCorrection` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCreateCorrectionAsync` | `POST /1/nfe/cartacorrecao/` | habilitada |
| `nfeCreateManifestation` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCreateManifestationAsync` | `POST /1/nfe/manifesta/` | habilitada |
| `nfeCancelDocument` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCancelDocumentAsync` | `PUT /1/nfe/cancelar/` | habilitada |
| `nfeVoidNumbering` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeVoidNumberingAsync` | `PUT /1/nfe/inutilizar/` | habilitada |
| `nfeCreatePaymentEvent` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCreatePaymentEventAsync` | `POST /1/nfe/conciliacao/` | habilitada |
| `nfeCancelPaymentEvent` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCancelPaymentEventAsync` | `PUT /1/nfe/conciliacao/cancelar/` | habilitada |
| `nfeCancelIbsCbsEvent` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCancelIbsCbsEventAsync` | `PUT /1/nfe/evento-ibs-cbs/cancelar/` | habilitada |
| `nfeListTaxClasses` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeListTaxClassesAsync` | `GET /1/nfe/classe-imposto/` | habilitada |
| `nfeDeleteTaxClasses` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeDeleteTaxClassesAsync` | `DELETE /1/nfe/classe-imposto/` | habilitada |
| `nfeSaveTaxClasses` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeSaveTaxClassesAsync` | `POST /1/nfe/classe-imposto/` | habilitada |
| `nfeIssueDocument` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeIssueDocumentAsync` | `POST /1/nfe/emissao/` | habilitada |
| `nfeIssueReturn` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeIssueReturnAsync` | `POST /1/nfe/devolucao/` | habilitada |
| `nfeIssueAdjustment` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeIssueAdjustmentAsync` | `POST /1/nfe/ajuste/` | habilitada |
| `nfeIssueComplement` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeIssueComplementAsync` | `POST /1/nfe/complementar/` | habilitada |
| `fiscalUpdateCompany` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `FiscalUpdateCompanyAsync` | `POST /1/nfe/empresa/` | habilitada |
| `fiscalCreateOrPollReport` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `FiscalCreateOrPollReportAsync` | `POST /1/nfe/relatorios/` | habilitada |
| `nfeCreateIbsCbsEvent` | `WebmaniaClient` com `WebmaniaCredentials.Company` | `NfeCreateIbsCbsEventAsync` | `POST /1/nfe/evento-ibs-cbs/` | habilitada |
| `nfeDownloadDanfe` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadDanfeAsync` | `GET /danfe/{identifier}/` | habilitada |
| `nfeDownloadSimpleDanfe` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadSimpleDanfeAsync` | `GET /danfe/simples/{identifier}/` | habilitada |
| `nfeDownloadLabelDanfe` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadLabelDanfeAsync` | `GET /danfe/etiqueta/{identifier}/` | habilitada |
| `nfeDownloadCorrectionPdf` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadCorrectionPdfAsync` | `GET /dacce/{identifier}/` | habilitada |
| `nfeDownloadCorrectionPdfSequence` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadCorrectionPdfSequenceAsync` | `GET /dacce/{identifier}/{event}/` | habilitada |
| `nfeDownloadXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadXmlAsync` | `GET /xmlnfe/{identifier}/` | habilitada |
| `nfeDownloadCancellationXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadCancellationXmlAsync` | `GET /xmlnfe/{identifier}/cancelamento` | habilitada |
| `nfeDownloadCorrectionXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadCorrectionXmlAsync` | `GET /xmlcce/{identifier}/` | habilitada |
| `nfeDownloadCorrectionXmlSequence` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadCorrectionXmlSequenceAsync` | `GET /xmlcce/{identifier}/{event}/` | habilitada |
| `nfeDownloadManifestationXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadManifestationXmlAsync` | `GET /xmlmde/{uuid}/` | habilitada |
| `nfeDownloadPaymentEventXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadPaymentEventXmlAsync` | `GET /xmleconf/{identifier}/` | habilitada |
| `nfeDownloadPaymentEventXmlSequence` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadPaymentEventXmlSequenceAsync` | `GET /xmleconf/{identifier}/{event}/` | habilitada |
| `nfeDownloadIbsCbsEventXml` | `WebmaniaClient` (`Api1FilesCompany`) | `NfeDownloadIbsCbsEventXmlAsync` | `GET /xmlevento/{uuid}/` | indisponível nesta versão (falha antes de enviar a requisição) |
| `fiscalDownloadReport` | `WebmaniaClient` (`Api1FilesCompany`) | `FiscalDownloadReportAsync` | `GET /relatorio/{uuid}/` | habilitada |
| `b2bV1CreateCompanies` | `WebmaniaClient` com `WebmaniaCredentials.Reseller` | `B2bV1CreateCompaniesAsync` | `POST /api/1/b2b/empresas/` | habilitada |
| `b2bV1ListCompanies` | `WebmaniaClient` com `WebmaniaCredentials.Reseller` | `B2bV1ListCompaniesAsync` | `GET /api/1/b2b/empresas/` | habilitada |
| `b2bV1GetProcessedRequests` | `WebmaniaClient` com `WebmaniaCredentials.Reseller` | `B2bV1GetProcessedRequestsAsync` | `GET /api/1/b2b/requests/` | habilitada |
| `emitterIssueCte` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueCteAsync` | `POST /2/cte/emissao` | habilitada |
| `emitterIssueSimplifiedCte` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueSimplifiedCteAsync` | `POST /2/cte/emissao/simplificada` | habilitada |
| `emitterCancelCte` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelCteAsync` | `PUT /2/cte/cancelar` | habilitada |
| `emitterCorrectCte` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCorrectCteAsync` | `POST /2/cte/correcao` | habilitada |
| `emitterConfirmCteDelivery` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterConfirmCteDeliveryAsync` | `POST /2/cte/entrega` | habilitada |
| `emitterCancelCteDelivery` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelCteDeliveryAsync` | `PUT /2/cte/entrega/cancelar` | habilitada |
| `emitterLinkCtePayment` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterLinkCtePaymentAsync` | `POST /2/cte/evento` | habilitada |
| `emitterCancelCtePaymentLink` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelCtePaymentLinkAsync` | `PUT /2/cte/evento/cancelar` | habilitada |
| `emitterGetCte` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetCteAsync` | `GET /2/cte/consulta/{identifier}` | habilitada |
| `emitterGetCtePaymentEvents` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetCtePaymentEventsAsync` | `GET /2/cte/consulta/evento/{identifier}` | habilitada |
| `emitterIssueDce` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueDceAsync` | `POST /2/dce/emissao` | habilitada |
| `emitterCancelDce` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelDceAsync` | `PUT /2/dce/cancelar` | habilitada |
| `emitterGetDce` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetDceAsync` | `GET /2/dce/consulta/{identifier}` | habilitada |
| `fileGetNfseXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfseXmlAsync` | `GET /xmlnfse/{identifier}` | habilitada |
| `fileGetNfsePdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfsePdfAsync` | `GET /nfse/{identifier}` | habilitada |
| `fileGetNfseRpsPdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfseRpsPdfAsync` | `GET /darps/{identifier}` | habilitada |
| `fileGetCteXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetCteXmlAsync` | `GET /xmlcte/{identifier}` | habilitada |
| `fileGetCteCancelXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetCteCancelXmlAsync` | `GET /xmlcte/{identifier}/cancelamento` | habilitada |
| `fileGetCtePdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetCtePdfAsync` | `GET /dacte/{identifier}` | habilitada |
| `fileGetCteCorrectionXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetCteCorrectionXmlAsync` | `GET /xmlcce/{identifier}` | habilitada |
| `fileGetCteCorrectionPdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetCteCorrectionPdfAsync` | `GET /dacce/{identifier}` | habilitada |
| `fileGetMdfeXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetMdfeXmlAsync` | `GET /xmlmdfe/{identifier}` | habilitada |
| `fileGetMdfePdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetMdfePdfAsync` | `GET /damdfe/{identifier}` | habilitada |
| `fileGetNfcomXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfcomXmlAsync` | `GET /xmlnfcom/{identifier}` | habilitada |
| `fileGetNfcomCancelXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfcomCancelXmlAsync` | `GET /xmlnfcom/{identifier}/cancelamento` | habilitada |
| `fileGetNfcomPdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetNfcomPdfAsync` | `GET /danfecom/{identifier}` | habilitada |
| `fileGetDceXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetDceXmlAsync` | `GET /xmldce/{identifier}` | habilitada |
| `fileGetDceCancelXml` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetDceCancelXmlAsync` | `GET /xmldce/{identifier}/cancelamento` | habilitada |
| `fileGetDcePdf` | `WebmaniaClient` (`Api2FilesCompanyBearer`) | `FileGetDcePdfAsync` | `GET /dace/{identifier}` | habilitada |
| `emitterGetNfseProviderStatus` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetNfseProviderStatusAsync` | `GET /2/nfse/status` | habilitada |
| `emitterGetNfcomServiceStatus` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetNfcomServiceStatusAsync` | `GET /2/nfcom/status` | habilitada |
| `emitterGetNfse` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetNfseAsync` | `GET /2/nfse/consulta/{identifier}` | habilitada |
| `emitterGetNfcom` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetNfcomAsync` | `GET /2/nfcom/consulta/{identifier}` | habilitada |
| `emitterGetNfcomPaymentEvents` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetNfcomPaymentEventsAsync` | `GET /2/nfcom/consulta/evento/{identifier}` | habilitada |
| `emitterManifestNfse` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterManifestNfseAsync` | `PUT /2/nfse/manifestar` | habilitada |
| `emitterIssueMdfe` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueMdfeAsync` | `POST /2/mdfe/emissao` | habilitada |
| `emitterCloseMdfe` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCloseMdfeAsync` | `PUT /2/mdfe/encerrar` | habilitada |
| `emitterCancelMdfe` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelMdfeAsync` | `PUT /2/mdfe/cancelar` | habilitada |
| `emitterAddMdfeConductor` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterAddMdfeConductorAsync` | `PUT /2/mdfe/condutor` | habilitada |
| `emitterGetMdfe` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterGetMdfeAsync` | `GET /2/mdfe/consulta/{key}` | habilitada |
| `monitorSearchDocuments` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorSearchDocumentsAsync` | `POST /2/monitor/documentos` | habilitada |
| `monitorGetDocument` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorGetDocumentAsync` | `GET /2/monitor/documentos/{fd}` | habilitada |
| `monitorListDocumentEvents` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorListDocumentEventsAsync` | `GET /2/monitor/documentos/{fd}/eventos` | habilitada |
| `monitorManifestDocument` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorManifestDocumentAsync` | `POST /2/monitor/documentos/{fd}/eventos` | habilitada |
| `monitorListReports` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorListReportsAsync` | `GET /2/monitor/documentos/relatorios` | habilitada |
| `monitorDownloadReport` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorDownloadReportAsync` | `GET /2/monitor/documentos/relatorios/{report}` | habilitada |
| `monitorImportDocument` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorImportDocumentAsync` | `POST /2/monitor/documentos/importacoes` | habilitada |
| `monitorGetImportStatus` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorGetImportStatusAsync` | `GET /2/monitor/documentos/importacoes/{uuid}` | habilitada |
| `monitorCreateCompany` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorCreateCompanyAsync` | `POST /2/monitor/empresas` | habilitada |
| `monitorUpdateCompany` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorUpdateCompanyAsync` | `PUT /2/monitor/empresas/{company}` | habilitada |
| `monitorCreateCompanyMunicipality` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorCreateCompanyMunicipalityAsync` | `POST /2/monitor/empresas/{company}/municipios-nfse` | habilitada |
| `monitorUpdateCompanyMunicipality` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorUpdateCompanyMunicipalityAsync` | `PUT /2/monitor/empresas/{company}/municipios-nfse/{download}` | habilitada |
| `monitorDeleteCompanyMunicipality` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorDeleteCompanyMunicipalityAsync` | `DELETE /2/monitor/empresas/{company}/municipios-nfse/{download}` | habilitada |
| `monitorListCompanies` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorListCompaniesAsync` | `GET /2/monitor/empresas` | habilitada |
| `monitorGetCompany` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorGetCompanyAsync` | `GET /2/monitor/empresas/{company}` | habilitada |
| `monitorListCompanyMunicipalities` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorListCompanyMunicipalitiesAsync` | `GET /2/monitor/empresas/{company}/municipios-nfse` | habilitada |
| `monitorGetCompanyMunicipality` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorGetCompanyMunicipalityAsync` | `GET /2/monitor/empresas/{company}/municipios-nfse/{download}` | habilitada |
| `monitorListAvailableMunicipalities` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorListAvailableMunicipalitiesAsync` | `GET /2/monitor/municipios-nfse` | habilitada |
| `monitorGetDownloadStatistics` | `WebmaniaClient` com `WebmaniaCredentials.Monitor` | `MonitorGetDownloadStatisticsAsync` | `GET /2/monitor/estatisticas/downloads` | habilitada |
| `emitterIssueNfcom` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueNfcomAsync` | `POST /2/nfcom/emissao` | habilitada |
| `emitterCancelNfcom` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelNfcomAsync` | `PUT /2/nfcom/cancelar` | habilitada |
| `emitterIssueNfse` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterIssueNfseAsync` | `POST /2/nfse/emissao` | habilitada |
| `emitterCancelNfse` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterCancelNfseAsync` | `PUT /2/nfse/cancelar` | habilitada |
| `emitterReplaceNfse` | `WebmaniaClient` com `WebmaniaCredentials.Issuer` | `EmitterReplaceNfseAsync` | `POST /2/nfse/substituir` | habilitada |
| `fiscalDownloadReportV2` | `WebmaniaClient` (`ReportCapability`) | `FiscalDownloadReportV2Async` | `GET /relatorios/{uuid}` | habilitada |
