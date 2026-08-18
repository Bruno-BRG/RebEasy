package com.rehabeasy.service;

import com.rehabeasy.model.PatientClinicalNote;
import com.rehabeasy.model.PatientClinicalNoteHistoryEntry;

import java.util.List;

public interface ClinicalNoteStore {
    PatientClinicalNote getClinicalNote(String patientId);

    void saveClinicalNote(String patientId, String content);

    List<PatientClinicalNoteHistoryEntry> getClinicalNoteHistory(String patientId);
}
