package com.rehabeasy;

import com.rehabeasy.ui.MainController;
import javafx.application.Application;
import javafx.fxml.FXMLLoader;
import javafx.scene.Parent;
import javafx.scene.Scene;
import javafx.scene.control.Alert;
import javafx.scene.control.ButtonType;
import javafx.stage.Stage;

public final class Main extends Application {
    private AppContext context;
    private MainController controller;

    public static void main(String[] args) {
        launch(args);
    }

    @Override
    public void start(Stage stage) throws Exception {
        try {
            context = AppContext.create();
            controller = new MainController(context);
            FXMLLoader loader = new FXMLLoader(Main.class.getResource("/com/rehabeasy/ui/main-view.fxml"));
            loader.setController(controller);
            Parent root = loader.load();

            Scene scene = new Scene(root, 1440, 900);
            scene.getStylesheets().add(Main.class.getResource("/com/rehabeasy/ui/styles.css").toExternalForm());
            stage.setTitle("RehabEasy - Java 25");
            stage.setMinWidth(1100);
            stage.setMinHeight(720);
            stage.setScene(scene);
            stage.setOnCloseRequest(event -> shutdown());
            stage.show();
        } catch (Exception exception) {
            showStartupError(exception);
            shutdown();
            throw exception;
        }
    }

    private void shutdown() {
        if (controller != null) {
            controller.shutdown();
        }
        if (context != null) {
            context.close();
        }
    }

    private static void showStartupError(Exception exception) {
        Alert alert = new Alert(
                Alert.AlertType.ERROR,
                exception.getMessage() == null ? "Falha ao iniciar o RehabEasy." : exception.getMessage(),
                ButtonType.OK);
        alert.setTitle("RehabEasy");
        alert.setHeaderText("Falha ao iniciar o aplicativo");
        alert.showAndWait();
    }
}
