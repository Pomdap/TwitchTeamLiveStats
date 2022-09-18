# TwitchTeamLiveStats

Il s'agit d'une application console qui utilise l'api [PubSub de Twitch](https://dev.twitch.tv/docs/pubsub) afin de récupérer le nombre de viewers pour une liste de chaines donnée.

Elle peut-être utilisé pour avoir un compteur commun lors d'un évennement ou pour votre collectif Twitch.

Vous pouvez télécharger la dernière version [directement ici](/releases/latest/download/TwitchTeamLiveStats.zip), ou sur la page des [releases](/releases).

## Configuration de l'application

La configuration se fait dans le fichier `appsettings.json`, voici le contenu par défaut :

    {
        "TeamSupervisor": {
            "ChannelIds": [ "123", "456", "789" ],
            "TimeoutInterval" : "00:03:00"
        },
        "TeamWritter": {
            "OutputDirectory": "./out/",
            "TotalViewersFile": "total.viewers",
            "LiveChannelCountFile": "live.channels",
            "LiveChannelTotalFile": "total.channels",
            "IndividualChannelFileFormat": "{0}.viewers"
        }
    }

### TeamSupervisor

Le tableau `ChannelIds` devra contenir la liste des identifiants des chaînes que vous voulez monitorer. Vous pouvez trouver la correspondace entre le nom d'une chaine et son id en utilisant des outils comme celui présent sur [streamweasels.com](https://www.streamweasels.com/tools/convert-twitch-username-to-user-id/).

La valeur `TimeoutInterval` permet de définir a partir de combien de temps une chaine est considérée comme n'étant plus en live, au cas ou l'application ne reçoit plus d'informations. En général Twitch envoie des informations toutes les 30 secondes.

### TeamWritter

La valeur `OutputDirectory` indique dans quel dossier les fichiers de sortie seront créés. Attention, ce dossier est automatiquement vidé au lancement de l'application.
La valeur `TotalViewersFile` indique comment sera nommé le fichier contenant le nombre total de viewers. Si omis le fichier ne sera pas créé.
La valeur `LiveChannelCountFile` indique comment sera nommé le fichier contenant le nombre de channels supervisés. Si omis le fichier ne sera pas créé.
La valeur `LiveChannelTotalFile` indique comment sera nommé le fichier contenant le nombre de channels actuellement en live. Si omis le fichier ne sera pas créé.
La valeur `IndividualChannelFileFormat` indique comment seront formatés les fichiers créés pour le total de viewers de chaques chaines. Si omis le fichier ne sera pas créé.

## Exemple

Je veux suivre les chaînes de Ultia, Ponce et AntoineDaniel.

Il faut que je mette les valeurs suivantes dans le tableau `ChannelIds` de mon fichier `appsettings.json` :

     "ChannelIds": [ "68594999", "50597026", "135468063" ],

Une fois l'application lancée, mon dossier contiendra les fichiers suivants :

    .
    +-- TwitchTeamLiveStats.exe
    +-- appsettings.json
    +-- out
        +-- total.channels          // Nombre de channels monitorés (3 dans notre cas)
        +-- live.channels           // Nombre de channels actuellement en live
        +-- total.viewers           // Nombre de viewers total pour les 3 channels
        +-- 68594999.viewers           // Nombre de viewers actuel pour Ponce
        +-- total.viewers           // Nombre de viewers actuel pour Ponce
        +-- total.viewers           // Nombre de viewers actuel pour Ponce

Ceux-ci seront mis à jour en temps réel et peuvent être utilisés pour créer une source Texte (GDI+).