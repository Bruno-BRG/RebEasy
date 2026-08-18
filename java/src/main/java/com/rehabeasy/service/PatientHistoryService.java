package com.rehabeasy.service;

import com.rehabeasy.model.PatientHistorySnapshot;

public interface PatientHistoryService {
    PatientHistorySnapshot getPatientHistory(String patientId);

    String buildHistoryReport(PatientHistorySnapshot history);
}
