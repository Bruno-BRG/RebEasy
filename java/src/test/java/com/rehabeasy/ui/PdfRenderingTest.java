package com.rehabeasy.ui;

import org.apache.pdfbox.Loader;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.rendering.PDFRenderer;
import org.junit.jupiter.api.Test;

import java.awt.image.BufferedImage;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertTrue;

class PdfRenderingTest {
    @Test
    void rendersRepositoryPdfFixtureForEmbeddedPreview() throws Exception {
        Path fixture = Path.of("..", "..", "vercel-api", "examples", "fixtures", "cvtug_smoke.pdf")
                .toAbsolutePath()
                .normalize();
        if (!Files.isRegularFile(fixture)) {
            return;
        }

        try (PDDocument document = Loader.loadPDF(fixture.toFile())) {
            assertTrue(document.getNumberOfPages() > 0);
            BufferedImage image = new PDFRenderer(document).renderImageWithDPI(0, 120);
            assertTrue(image.getWidth() > 0);
            assertTrue(image.getHeight() > 0);
        }
    }
}
