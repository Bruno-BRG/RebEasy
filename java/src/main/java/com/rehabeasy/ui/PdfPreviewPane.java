package com.rehabeasy.ui;

import javafx.application.Platform;
import javafx.embed.swing.SwingFXUtils;
import javafx.geometry.Insets;
import javafx.geometry.Pos;
import javafx.scene.control.Label;
import javafx.scene.control.ScrollPane;
import javafx.scene.image.Image;
import javafx.scene.image.ImageView;
import javafx.scene.layout.BorderPane;
import javafx.scene.layout.VBox;
import org.apache.pdfbox.Loader;
import org.apache.pdfbox.pdmodel.PDDocument;
import org.apache.pdfbox.rendering.PDFRenderer;

import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class PdfPreviewPane extends BorderPane {
    private static final int MAX_RENDERED_PAGES = 40;
    private static final float RENDER_DPI = 120;

    private final Label placeholder = new Label("Selecione um registro com PDF para visualizar.");
    private final VBox pageContainer = new VBox(12);
    private final ScrollPane scrollPane = new ScrollPane(pageContainer);
    private final ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();

    public PdfPreviewPane() {
        placeholder.setWrapText(true);
        placeholder.setAlignment(Pos.CENTER);
        placeholder.setMaxWidth(Double.MAX_VALUE);
        placeholder.setMaxHeight(Double.MAX_VALUE);
        placeholder.setPadding(new Insets(24));
        pageContainer.setAlignment(Pos.TOP_CENTER);
        pageContainer.setPadding(new Insets(12));
        scrollPane.setFitToWidth(true);
        scrollPane.setVisible(false);
        setCenter(placeholder);
        setStyle("-fx-background-color: white; -fx-border-color: #C5D4DC; -fx-border-radius: 8;");
    }

    public void load(String pdfLocalPath) {
        clear();
        if (pdfLocalPath == null || pdfLocalPath.isBlank()) {
            return;
        }
        Path path = Path.of(pdfLocalPath);
        if (!Files.isRegularFile(path)) {
            placeholder.setText("O PDF local nao foi encontrado.");
            return;
        }

        placeholder.setText("Carregando PDF...");
        executor.submit(() -> {
            try {
                List<Image> pages = render(path.toFile());
                Platform.runLater(() -> {
                    pageContainer.getChildren().clear();
                    for (Image page : pages) {
                        ImageView imageView = new ImageView(page);
                        imageView.setPreserveRatio(true);
                        imageView.setFitWidth(760);
                        imageView.setSmooth(true);
                        pageContainer.getChildren().add(imageView);
                    }
                    scrollPane.setVisible(true);
                    setCenter(scrollPane);
                });
            } catch (Exception exception) {
                Platform.runLater(() -> {
                    placeholder.setText("Nao foi possivel abrir o PDF: " + exception.getMessage());
                    scrollPane.setVisible(false);
                    setCenter(placeholder);
                });
            }
        });
    }

    public void clear() {
        pageContainer.getChildren().clear();
        scrollPane.setVisible(false);
        placeholder.setText("Selecione um registro com PDF para visualizar.");
        setCenter(placeholder);
    }

    private static List<Image> render(File file) throws Exception {
        List<Image> pages = new ArrayList<>();
        try (PDDocument document = Loader.loadPDF(file)) {
            PDFRenderer renderer = new PDFRenderer(document);
            int pageCount = Math.min(document.getNumberOfPages(), MAX_RENDERED_PAGES);
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++) {
                BufferedImage image = renderer.renderImageWithDPI(pageIndex, RENDER_DPI);
                pages.add(SwingFXUtils.toFXImage(image, null));
            }
        }
        return pages;
    }

    public void shutdown() {
        executor.shutdownNow();
    }
}
