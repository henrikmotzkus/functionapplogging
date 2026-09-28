using System.Text.Json;
using Azure.Storage.Queues.Models;
using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Models;
using LoggingDemo.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LoggingDemo.Functions.Functions;

/// <summary>
/// Bildet die Queue-Eingangsgrenze des Logging-PoC ab und startet die asynchrone
/// Verarbeitung eines zuvor per HTTP angenommenen Arbeitsauftrags.
/// </summary>
/// <remarks>
/// Die Function trennt Azure-Functions-spezifische Aufgaben wie Queue-Binding,
/// Deserialisierung, Zustellinformationen und Retry-Signalisierung von der eigentlichen
/// Geschäftslogik im <see cref="IWorkItemService"/>. Dadurch kann der PoC klar zeigen,
/// welche Logs an der Function-Grenze und welche in der Anwendungsschicht entstehen.
///
/// <paramref name="workItemService"/> und der typisierte Logger werden vom .NET Isolated
/// Worker per Dependency Injection bereitgestellt. Der Logger verwendet die Kategorie
/// "LoggingDemo.Functions.Functions.ProcessWorkItemFunction", sodass seine Einträge in
/// Application Insights gezielt von anderen Schichten unterschieden werden können.
/// </remarks>
public sealed class ProcessWorkItemFunction(
    IWorkItemService workItemService,
    ILogger<ProcessWorkItemFunction> logger)
{
    /// <summary>
    /// Verarbeitet eine von Azure Storage Queue zugestellte Nachricht.
    /// </summary>
    /// <param name="queueMessage">
    /// Die Nachricht einschließlich Azure-seitiger Metadaten wie Queue Message ID und
    /// Dequeue Count. Diese Metadaten sind wichtig, um Zustellungen und Retries zu erklären.
    /// </param>
    /// <param name="context">
    /// Der Ausführungskontext des Isolated Workers. Sein CancellationToken wird an die
    /// Anwendungsschicht weitergegeben, damit ein Host-Shutdown oder Abbruch respektiert wird.
    /// </param>
    [Function(nameof(ProcessWorkItem))]
    public async Task ProcessWorkItem(
        // Der QueueTrigger lässt den Functions Host die konfigurierte Queue überwachen.
        // StorageNames enthält keine fest codierten Umgebungsnamen: Queue und Connection
        // werden aus der Function-Konfiguration aufgelöst und können je Umgebung variieren.
        //
        // Eine erfolgreich beendete Methode bestätigt die Verarbeitung. Verlässt dagegen
        // eine Exception die Methode, behandelt der Host die Zustellung als fehlgeschlagen.
        [QueueTrigger(StorageNames.WorkItemsQueue, Connection = StorageNames.Connection)]
        QueueMessage queueMessage,
        FunctionContext context)
    {
        WorkItemMessage workItem;
        try
        {
            // Die SubmitWorkItemFunction legt das fachliche WorkItemMessage als JSON in der
            // Queue ab. Hier wird dasselbe JSON mit denselben JsonDefaults zurück in das
            // typisierte Modell übersetzt. So bleiben Serialisierung und Deserialisierung
            // über die asynchrone Systemgrenze hinweg konsistent.
            //
            // Ein formal gültiger JSON-Wert "null" ist technisch deserialisierbar, stellt
            // aber keinen Auftrag dar. Deshalb wird dieser Fall explizit in eine
            // JsonException umgewandelt und wie fehlerhaftes JSON behandelt.
            workItem = JsonSerializer.Deserialize<WorkItemMessage>(
                queueMessage.Body.ToString(),
                JsonDefaults.Options)
                ?? throw new JsonException("Queue message body was empty.");
        }
        catch (JsonException exception)
        {
            // Ohne erfolgreich deserialisiertes WorkItem sind dessen CorrelationId,
            // MessageId und Scenario nicht verfügbar. Dieses Log entsteht deshalb bewusst
            // vor dem fachlichen Logging-Scope und verwendet nur sichere Queue-Metadaten.
            //
            // QueueMessageId identifiziert die physische Azure-Queue-Nachricht.
            // DequeueCount zeigt, wie oft sie bereits zugestellt wurde. Gemeinsam mit
            // Exception und Stacktrace lässt sich eine nicht lesbare Nachricht diagnostizieren.
            logger.LogError(
                exception,
                "Queue message {QueueMessageId} could not be deserialized on delivery {DequeueCount}.",
                queueMessage.MessageId,
                queueMessage.DequeueCount);

            // Die Exception darf nicht verschluckt werden: Nur eine fehlgeschlagene
            // Function-Ausführung veranlasst den Functions Host zu einem erneuten Versuch.
            // Gemäß host.json wird die Nachricht nach maximal drei Zustellungen in die vom
            // Host verwaltete Poison Queue verschoben. Ein return würde fälschlich Erfolg
            // signalisieren und die defekte Nachricht aus der Ursprungsqueue entfernen.
            throw;
        }

        // Nach erfolgreicher Deserialisierung stehen die fachlichen Korrelationseigenschaften
        // zur Verfügung. BeginScope ergänzt alle nachfolgenden Logs dieser Function sowie
        // die innerhalb desselben Ausführungskontexts erzeugten Service-Logs um strukturierte
        // Custom Dimensions für Application Insights.
        //
        // DemoLayer markiert diese Meldungen als FunctionBoundary. CorrelationId verbindet
        // den Queue-Teil mit dem ursprünglichen HTTP-Aufruf. MessageId identifiziert den
        // fachlichen Auftrag, während QueueMessageId die konkrete Transportnachricht
        // bezeichnet. DequeueCount macht Retry-Versuche direkt in der Telemetrie sichtbar.
        //
        // "using" beendet den Scope auch bei einer Exception zuverlässig. Damit können
        // Eigenschaften dieser Zustellung nicht in eine spätere Ausführung gelangen.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DemoLayer"] = "FunctionBoundary",
            ["CorrelationId"] = workItem.CorrelationId,
            ["MessageId"] = workItem.MessageId,
            ["QueueMessageId"] = queueMessage.MessageId,
            ["Scenario"] = workItem.Scenario.ToString(),
            ["DequeueCount"] = queueMessage.DequeueCount
        });

        // Dieses Information-Log markiert den Beginn jedes Verarbeitungsversuchs. Bei einem
        // Retry erscheint es erneut mit derselben fachlichen MessageId und CorrelationId,
        // aber einem höheren DequeueCount. Genau dieses Verhalten soll der PoC sichtbar machen.
        logger.LogInformation("Dequeued work item for processing.");

        try
        {
            // Die Function delegiert die fachliche Verarbeitung bewusst an den Service.
            // Dort entstehen Logs mit DemoLayer "Application"; weitere Abhängigkeiten können
            // eigene Infrastructure-Logs erzeugen. Da der aktive Scope weitergereicht wird,
            // bleiben die gemeinsamen Korrelationseigenschaften über alle Schichten erhalten.
            //
            // Der DequeueCount wird zusätzlich als Methodenargument übergeben, damit
            // deterministische Szenarien ihr Verhalten vom aktuellen Retry-Versuch abhängig
            // machen können. Das CancellationToken verhindert unnötige Weiterarbeit, wenn
            // der Functions Host die laufende Ausführung beendet.
            await workItemService.ProcessAsync(
                workItem,
                queueMessage.DequeueCount,
                context.CancellationToken);
        }
        catch (WorkItemProcessingException exception)
        {
            // Erwartete Fehler der Anwendungsschicht werden an der Function-Grenze erneut
            // protokolliert. So zeigt die Demo sowohl das fachliche Log aus dem Service als
            // auch die Sicht der auslösenden Function. FailureType wird als strukturiertes
            // Feld erfasst und kann in Application Insights gruppiert werden.
            logger.LogError(
                exception,
                "Work item processing failed with failure type {FailureType}.",
                exception.GetType().Name);

            // Auch hier muss die Exception den Functions Host erreichen. Der Host setzt die
            // Nachricht für die in host.json konfigurierte Dauer unsichtbar, stellt sie
            // anschließend erneut zu und verschiebt sie nach Erreichen von maxDequeueCount
            // in die Poison Queue. Dadurch demonstrieren transient- und poison-Szenarien
            // reales Retry-Verhalten statt eines nur protokollierten Scheinerfolgs.
            throw;
        }
    }
}
