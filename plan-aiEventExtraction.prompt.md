## Piano: Estrazione eventi con AI

Il flusso aggiunge l’estrazione come modalità di precompilazione: l’utente inserisce l’URL della CFP, la Function delega recupero pagina e inferenza a `TalksOps.AI`, poi i risultati confluiscono nell’editor eventi già presente. Il piano è salvato in `/memories/session/plan.md`.

**Passi**
1. Creare `TalksOps.AI` e `TalksOps.AI.Tests`. Aggiungere `ExtractEventDataPrompt.txt` alla libreria e copiarlo nell’output usato dalle Functions, mantenendo invariato l’attuale [ExtractEventDataPrompt.md](ExtractEventDataPrompt.md).
2. Aggiungere DTO nullable per richiesta, risultato ed errori; esporre il metodo nel client API seguendo il contratto attuale con `ApiRequest<T>`.
3. Implementare recupero HTML e chiamata a Foundry con Managed Identity. Applicare i placeholder del prompt, deserializzare il JSON previsto, rifiutare risultati tutti null e convertire le date `DD/MM/YYYY` con parsing esatto e cultura invariabile. Registrare deployment, token input/output e utente, mai HTML o prompt.
4. Aggiungere una Function HTTP con risposta strutturata: codice errore, messaggio leggibile e status appropriato. Applicare `System.Threading.RateLimiting`, partizionato per utente e configurato con default 1 richiesta/minuto.
5. In [Events.razor](src/TalksOps.Web/Components/Pages/Events.razor), aggiungere la scelta di estrazione e il dialog URL, visibili solo con feature flag attivo. Gestire caricamento ed errori; precompilare i valori presenti e lasciare vuoti gli altri, riusando l’editor e `SaveEventAsync`.
6. Creare un modulo Bicep Foundry richiamato da [main.bicep](infra/main.bicep), con progetto `c4pextraction` e deployment `gpt-4.1-nano` Global Standard. Passare endpoint, progetto e deployment al backend e assegnare all’identità della Function il ruolo minimo necessario. Configurare il rate limit backend e il feature flag frontend, disattivato per default.
7. Aggiungere test AI, ApiClient, Functions e Web: gestione SSRF, redirect e limiti di fetch; risposte Foundry malformate o vuote; mapping errori e 429; feature flag, precompilazione parziale e creazione manuale invariata.
8. Verificare test e build .NET, compilazione Bicep e flussi UI manuali. Il piano non include provisioning Azure.

**Riferimenti**
- UI ed editor esistente: [Events.razor](src/TalksOps.Web/Components/Pages/Events.razor)
- Contratto API: [ITalksOpsApiClient.cs](src/TalksOps.ApiClient/ITalksOpsApiClient.cs), [TalksOpsApiClient.cs](src/TalksOps.ApiClient/TalksOpsApiClient.cs)
- Endpoint, utente e DI: [EventsFunctions.cs](src/TalksOps.Functions/EventsFunctions.cs), [RequestCurrentUserContext.cs](src/TalksOps.Functions/RequestCurrentUserContext.cs), [Program.cs](src/TalksOps.Functions/Program.cs)
- Infrastruttura: [main.bicep](infra/main.bicep), [backend.bicep](infra/modules/backend.bicep), [frontend.bicep](infra/modules/frontend.bicep), [identity.bicep](infra/modules/identity.bicep)

**Decisioni e vincoli**
- Rate limit per istanza, come scelto: con lo scale-out il limite aggregato può aumentare; non è previsto contatore distribuito.
- Aggiungere il file `.txt` richiesto senza rimuovere il Markdown esistente.
- Proteggere il fetch da SSRF: consentire solo HTTP(S) pubblico, validare indirizzi e redirect, imporre timeout e dimensione massima.
- Prima dell’implementazione, verificare SDK Foundry, API version Bicep, disponibilità regionale del modello e ruolo RBAC minimo. Verificare anche che l’identità corrente sia affidabile per rate limit e telemetria.

**Verifica**
- Test mirati per `TalksOps.AI`, ApiClient, Functions e Web; poi `dotnet build TalksOps.sln` e `dotnet test TalksOps.sln`.
- `az bicep build --file infra/main.bicep`.
- Prova manuale con flag disattivato, estrazione riuscita/parziale, errori e rate limit, e creazione manuale.
