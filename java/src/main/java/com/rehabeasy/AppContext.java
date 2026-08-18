package com.rehabeasy;

import com.rehabeasy.integration.ApiPayloadImportService;
import com.rehabeasy.persistence.SqliteRecordStore;
import com.rehabeasy.service.DefaultPatientHistoryService;
import com.rehabeasy.service.PatientHistoryService;
import com.rehabeasy.service.PayloadImportService;
import com.rehabeasy.service.RecordStore;

public final class AppContext implements AutoCloseable {
    private final AppConfig config;
    private final SqliteRecordStore sqliteRecordStore;
    private final PayloadImportService payloadImportService;
    private final PatientHistoryService patientHistoryService;

    private AppContext(
            AppConfig config,
            SqliteRecordStore sqliteRecordStore,
            PayloadImportService payloadImportService,
            PatientHistoryService patientHistoryService
    ) {
        this.config = config;
        this.sqliteRecordStore = sqliteRecordStore;
        this.payloadImportService = payloadImportService;
        this.patientHistoryService = patientHistoryService;
    }

    public static AppContext create() {
        AppConfig config = AppConfig.fromEnvironment();
        SqliteRecordStore store = new SqliteRecordStore(config.databasePath());
        store.initialize();
        return new AppContext(
                config,
                store,
                new ApiPayloadImportService(config),
                new DefaultPatientHistoryService(store, store));
    }

    public AppConfig config() {
        return config;
    }

    public RecordStore recordStore() {
        return sqliteRecordStore;
    }

    public SqliteRecordStore sqliteRecordStore() {
        return sqliteRecordStore;
    }

    public PayloadImportService payloadImportService() {
        return payloadImportService;
    }

    public PatientHistoryService patientHistoryService() {
        return patientHistoryService;
    }

    @Override
    public void close() {
        sqliteRecordStore.close();
    }
}
