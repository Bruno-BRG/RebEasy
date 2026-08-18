package com.rehabeasy.service;

import com.rehabeasy.model.ApiPayloadImportResult;

import java.util.List;

public interface PayloadImportService {
    ApiPayloadImportResult importPayload(String payloadId);

    ApiPayloadImportResult importNextPayload();

    List<ApiPayloadImportResult> importAllPendingPayloads();
}
