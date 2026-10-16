# Extrazione dati evento con AI Generativa

La funzionalita' permette all'utente aggiungere un nuovo evento all'applicazione estraendo i dati dalla pagina della call for paper utilizzando modelli di AI Generativa.

## Flusso della funzionalita'

- Il tasto "New event +" presente nella pagina di ricerca deve permettere di poter selezionare l'opzione di estrazione da pagina della call for paper (ad esempio potrebbe essere prevista una combo che mostra l'opzione se l'utente la apre)
- Quanto l'utente sceglie l'opzione di estrazione dati dalla pagina della call for paper viene seguito il seguente flusso applicativo:
    1. Viene aperto un popup in cui si richiede l'url della pagina contenente la call for paper. Il popup presenta il tasto estrai;
    2. L'applicazione recupera l'HTML della pagina web della call for paper
    3. L'applicazione crea un prompt utilizzando il testo contenuto nel file ExtractEventDataPrompt.txt sostituendo i due placeholder:
        [SOURCE_PAGE_URL] url della pagina della call for paper
        [HTML_CONTENT] HTML della pagina
    4. L'applicazione invia il prompt con un opportuno System Prompt ad un modello hostato in Microsoft Foundry (il nome del deplyment del modello, l'url del foundry e il nome del progetto sono presenti nella configurazione).
    5. Il JSON ritornato dal modello viene deserializzato in un oggetto evento e viene aperto il pop-up di inserimento dell'evento con i dati ricavati dalla pagina. Vengono visualizzati solo i dati del JSON che non sono null. L'utente dovra' inserire i dati che non sono estratti
    6. Da questo momento in poi la funzionalita' e' la stessa dell'inserimento manuale di un evento.
- In caso di errore nell'estrazione dei dati (pagina non raggiungibile, dati non estratti - tutti i campi null -, autenticazione necessaria e via dicendo), l'api deve riportare un codice di errore e un messaggio parlante che sara' utilizzato dal front-end per fornire informazioni all'utente.

## Architettura

Il front end utilizza una API esposta dalle Fucntion per l'estrazione. La risorsa Azure Function accede a Microsoft Foundry utilizzando una Managed Identity e un ruolo opportuno RBAC.

L'infrastruttura attuale prevede:
- l'aggiunta di un Microsoft Foundry con un progetto (chiamato "c4pextraction")
- depolyment (chiamato gpt-4.1-nano) di un modello gpt-4.1-nano in modalita' standard global

Tutta le risorse relative aFoundry vanno relegate in un Bicep richiamato dal main.bicep

## Limitazioni
La funzionaita' puo' essere disattivata da configurazione con una chiave di configurazione del frontend. Nel caso in cui sia disattivata, l'opzione non e' visibile nella pagina di ricerca.
La funzionalita' ha un rate limit per utente. Il back end prevede una chiave di configurazione (con default 1) che indica il numero di chiamate alla funzione per minuto per utente. Tale funzionalita' deve essere implementata nel backend (Azure Functions). Per il rate limit cerca di utilizzare classi del framework .net. 

## Altro
L'API di estrazione, internamente, nel momento in cui richiama Foundry, scrive nel log le seguenti info: modello chiamato, token in input e token in output e nome utente. Questi dati possono servire, in caso di multi-utente per capire l'utilizzo dei token lato back-end

## Codice
Le funzionalita' di estrazione con AI vanno inserite in un nuovo progetto C# (library) dal nome TalksOps.AI.
L'API viene implementata con delle functions in TalksOps.Functions
La funzionalita' deve essere mappata in TalksOps.ApiClient con gli opportuni DTO.
Crea un progetto di test per le nuove classi TalksOps.AI.Tests.
Usa Foundry SDK per richiamare il modello in Microsoft Foundry.
Il prompt deve essere presente in un file di testo chiamato ExtractEventDataPrompt.txt e copiato assieme alla libreria nelle functions.