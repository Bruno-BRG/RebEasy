package com.rehabeasy.service;

import com.rehabeasy.model.RehabEasyRecord;

import java.util.List;

public interface RecordStore {
    void saveRecords(List<RehabEasyRecord> records);

    List<RehabEasyRecord> search(String query);

    List<RehabEasyRecord> getRecordsByPatientId(String patientId);

    void deleteRecord(String id);
}
