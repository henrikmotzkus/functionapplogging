using System.Net;
using System.Text.Json;
using LoggingDemo.Functions.Application;
using LoggingDemo.Functions.Models;
using LoggingDemo.Functions.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace LoggingDemo.Functions.Functions;

/// <summary>
/// Bildet die HTTP-Eingangsgrenze des Logging-PoC ab.
/// </summary>
/// <remarks>
/// Die Function nimmt einen Arbeitsauftrag synchron per HTTP entgegen, validiert ihn und
/// übergibt ihn anschließend zur asynchronen Verarbeitung an eine Storage Queue. Diese
/// Aufgabentrennung ist für den PoC wichtig: Anhand derselben Korrelations-ID lässt sich
/// beobachten, welche Telemetrie an der HTTP-Grenze und welche erst in nachgelagerten
/// Anwendungs- und Infrastrukturkomponenten entsteht.
///
/// Der typisierte Logger wird vom .NET Isolated Worker per Dependency Injection
/// bereitgestellt. Sein Typ bestimmt zugleich die Logger-Kategorie
/// "LoggingDemo.Functions.Functions.SubmitWorkItemFunction", nach der in Application
/// Insights gefiltert werden kann.
/// </remarks>
public sealed class SubmitWorkItemFunction(ILogger<SubmitWorkItemFunction> logger)
{
    /// <summary>
    /// Nimmt einen Arbeitsauftrag entgegen und gibt bei erfolgreicher Validierung sowohl
    /// eine HTTP-202-Antwort als auch eine Queue-Nachricht aus.
    /// </summary>
    /// <param name="request">Die eingehende HTTP-Anfrage einschließlich Body und Headern.</param>
    /// <param name="context">
    /// Der Ausführungskontext des Isolated Workers. Aus ihm wird unter anderem das
    /// CancellationToken der aktuellen Function-Ausführung übernommen.
    /// </param>
    /// <returns>
    /// Ein Multi-Output-Objekt. <see cref="SubmitWorkItemOutput.HttpResponse"/> wird an den
    /// HTTP-Aufrufer zurückgegeben; <see cref="SubmitWorkItemOutput.QueueMessage"/> wird
    /// durch das Queue-Output-Binding in Azure Storage geschrieben.
    /// </returns>
    [Function(nameof(SubmitWorkItem))]
    public async Task<SubmitWorkItemOutput> SubmitWorkItem(
        // AuthorizationLevel.Function verlangt einen Function Key. Das ist ein technischer
        // Zugriffsschutz für die Demo, aber keine Benutzeranmeldung mit Entra ID, OAuth
        // oder Benutzername und Passwort.
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "work-items")] HttpRequestData request,
        FunctionContext context)
    {
        WorkItemSubmission? submission;
        try
        {
            // Der Body wird direkt aus dem Request-Stream gelesen. Die zentral definierten
            // JsonDefaults sorgen dafür, dass HTTP-Eingang und spätere Queue-Nachricht
            // dieselben Serialisierungsregeln verwenden.
            //
            // Das CancellationToken wird weitergereicht, damit ein Abbruch der Function
            // auch das asynchrone Lesen beendet und keine unnötige Arbeit fortgesetzt wird.
            submission = await JsonSerializer.DeserializeAsync<WorkItemSubmission>(
                request.Body,
                JsonDefaults.Options,
                context.CancellationToken);
        }
        catch (JsonException exception)
        {
            // Diese Meldung wird absichtlich VOR dem Logging-Scope erzeugt: Bei ungültigem
            // JSON konnten Felder wie Scenario oder eine CorrelationId aus dem Body noch
            // nicht zuverlässig gelesen werden. Der PoC demonstriert damit, dass ein Log
            // nur den Kontext enthalten kann, der zum Entstehungszeitpunkt bekannt ist.
            //
            // Die Exception wird mitgegeben, damit Application Insights neben der
            // verständlichen Meldung auch Typ, Stacktrace und technische Ursache erfasst.
            logger.LogWarning(exception, "Request body was not valid JSON.");
            return await InvalidAsync(
                request,
                GetCorrelationId(request, null),
                "Request body must contain valid JSON.",
                context.CancellationToken);
        }

        // Die Korrelations-ID verbindet den synchronen HTTP-Aufruf mit der asynchronen
        // Queue-Verarbeitung. Sie wird bevorzugt aus dem Body, danach aus dem HTTP-Header
        // übernommen und nur dann neu erzeugt, wenn der Aufrufer keine ID geliefert hat.
        var correlationId = GetCorrelationId(request, submission?.CorrelationId);

        // Die Validierung ist eine Aufgabe der Systemgrenze: Ungültige Aufträge sollen
        // abgewiesen werden, bevor sie die Queue und damit nachgelagerte Schichten erreichen.
        // Der "validation"-Fall ist absichtlich deterministisch, damit er in einer Demo
        // jederzeit reproduzierbar als Warning plus HTTP 400 gezeigt werden kann.
        var validation = WorkItemSubmissionValidator.Validate(submission);

        // BeginScope ergänzt alle nachfolgenden Logs dieser Function-Ausführung um
        // strukturierte Eigenschaften. Diese Werte landen in Application Insights als
        // Custom Dimensions und sind dort unabhängig vom Meldungstext filter- und gruppierbar.
        //
        // DemoLayer kennzeichnet die erzeugende Architekturschicht. Dadurch können die Logs
        // dieser HTTP-Grenze von Application- und Infrastructure-Logs unterschieden werden.
        // "unknown" hält das Schema auch dann stabil, wenn kein gültiges Szenario vorliegt.
        //
        // Der Scope wird durch "using" am Methodenende beziehungsweise bei einem frühen
        // return sicher beendet und beeinflusst deshalb keine spätere Function-Ausführung.
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["DemoLayer"] = "FunctionBoundary",
            ["CorrelationId"] = correlationId,
            ["Scenario"] = submission?.Scenario ?? "unknown"
        });

        if (!validation.IsValid)
        {
            // Fachlich ungültige Eingaben sind erwartbare Client-Fehler und werden daher als
            // Warning statt als Error protokolliert. Das strukturierte Template bewahrt
            // ValidationError als eigenes Telemetriefeld, anstatt nur einen fertigen String
            // zu erzeugen. Es wird keine Queue-Nachricht geschrieben.
            logger.LogWarning("Work item submission was rejected: {ValidationError}", validation.Error);
            return await InvalidAsync(
                request,
                correlationId,
                validation.Error!,
                context.CancellationToken);
        }

        // Erst nach erfolgreicher Validierung wird die interne Queue-Nachricht aufgebaut.
        // MessageId identifiziert den einzelnen Auftrag, während CorrelationId eine gesamte
        // fachliche Ablaufkette identifiziert. Der UTC-Zeitstempel erlaubt später unter
        // anderem die Messung der Wartezeit zwischen HTTP-Annahme und Queue-Verarbeitung.
        //
        // Der Null-Forgiving-Operator ist hier zulässig, weil der Validator nur ein gültiges
        // Ergebnis liefert, wenn Submission und Payload vorhanden sind. Trim verhindert,
        // dass bedeutungslose Leerzeichen in die nachgelagerte Verarbeitung gelangen.
        var workItem = new WorkItemMessage(
            Guid.NewGuid(),
            correlationId,
            validation.Scenario,
            submission!.Payload!.Trim(),
            DateTimeOffset.UtcNow);

        // HTTP 202 Accepted drückt bewusst aus, dass der Auftrag angenommen, aber noch nicht
        // abgeschlossen wurde. Die tatsächliche Verarbeitung erfolgt entkoppelt durch die
        // Queue-Trigger-Function. CorrelationId und MessageId ermöglichen dem Aufrufer und
        // dem Betreiber, die weitere Verarbeitung in der Telemetrie wiederzufinden.
        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(
            new ApiResponse("accepted", correlationId, workItem.MessageId),
            context.CancellationToken);

        // Das Information-Log markiert den erfolgreichen Abschluss der Verantwortung dieser
        // HTTP-Function. Platzhalter erzeugen strukturierte Dimensionen für MessageId und
        // QueueName. Die tatsächliche Queue-Verarbeitung erzeugt anschließend eigene Logs
        // mit derselben CorrelationId, aber anderen DemoLayer-Werten.
        logger.LogInformation(
            "Accepted work item {MessageId} for queue {QueueName}.",
            workItem.MessageId,
            StorageNames.WorkItemsQueue);

        // SubmitWorkItemOutput ist ein Azure-Functions-Multi-Output:
        // - HttpResponse wird unmittelbar an den HTTP-Client gesendet.
        // - QueueMessage wird vom QueueOutput-Attribut der Output-Klasse gebunden.
        //
        // Die Function schreibt daher nicht selbst mit einem Queue-SDK. Das Binding hält den
        // Beispielcode auf die fachliche Übergabe fokussiert und erzeugt gleichzeitig
        // Functions-Host-Telemetrie für den Binding- und Queue-Vorgang.
        return new SubmitWorkItemOutput
        {
            HttpResponse = response,
            QueueMessage = JsonSerializer.Serialize(workItem, JsonDefaults.Options)
        };
    }

    /// <summary>
    /// Erstellt die einheitliche HTTP-400-Antwort für syntaktisch oder fachlich ungültige
    /// Eingaben und unterdrückt die Queue-Ausgabe.
    /// </summary>
    /// <remarks>
    /// Die gemeinsame Hilfsmethode verhindert, dass Fehlerantworten für ungültiges JSON und
    /// für Validierungsfehler auseinanderlaufen. Eine <c>null</c>-QueueMessage ist relevant:
    /// Das Output-Binding legt dadurch keinen Auftrag in der Queue ab.
    /// Das Logging erfolgt bewusst beim jeweiligen Aufrufer dieser Methode, weil dort
    /// Ursache, Exception und verfügbarer Scope bekannt sind.
    /// </remarks>
    private static async Task<SubmitWorkItemOutput> InvalidAsync(
        HttpRequestData request,
        string correlationId,
        string error,
        CancellationToken cancellationToken)
    {
        // Auch Fehlerantworten liefern die Korrelations-ID zurück. Damit kann ein Kunde
        // selbst bei HTTP 400 den passenden Warning-Eintrag in Application Insights suchen.
        var response = request.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(
            new ApiResponse("validationFailed", correlationId, Error: error),
            cancellationToken);

        return new SubmitWorkItemOutput
        {
            HttpResponse = response,
            QueueMessage = null
        };
    }

    /// <summary>
    /// Bestimmt eine stabile Korrelations-ID anhand einer klaren Prioritätsreihenfolge.
    /// </summary>
    /// <remarks>
    /// Eine vom Client im JSON-Body gesetzte ID hat Vorrang, weil sie Teil des fachlichen
    /// Auftrags ist. Der Header unterstützt übliche HTTP-Korrelationsmechanismen. Eine neue
    /// GUID stellt sicher, dass auch Clients ohne eigene Korrelation vollständig im PoC
    /// verfolgt werden können. Die Methode erzeugt keine Logs, damit die ID zuerst bestimmt
    /// und anschließend als strukturierter Scope auf alle relevanten Meldungen gelegt wird.
    /// </remarks>
    private static string GetCorrelationId(HttpRequestData request, string? suppliedCorrelationId)
    {
        if (!string.IsNullOrWhiteSpace(suppliedCorrelationId))
        {
            // Führende und nachfolgende Leerzeichen würden sonst zu schwer auffindbaren,
            // scheinbar unterschiedlichen Korrelationswerten in der Telemetrie führen.
            return suppliedCorrelationId.Trim();
        }

        if (request.Headers.TryGetValues("x-correlation-id", out var values))
        {
            // Für diesen PoC wird der erste nicht-leere Headerwert verwendet. Normalerweise
            // sendet ein Client genau einen x-correlation-id-Header.
            var headerValue = values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue.Trim();
            }
        }

        // Das "D"-Format erzeugt die kanonische GUID-Darstellung mit Bindestrichen und sorgt
        // dadurch für ein konsistentes Format in Response, Queue-Nachricht und Telemetrie.
        return Guid.NewGuid().ToString("D");
    }
}
