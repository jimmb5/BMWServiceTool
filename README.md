# G30 Service Tool

Suomenkielinen Windows-sovellus oman BMW G30:n (2018) huoltojen kirjaamiseen. Versio **0.1.1**.

## Käynnistä

Jos `artifacts/app/G30ServiceTool.exe` on jo rakennettu, avaa se tai kaksoisnapsauta projektin `Kaynnista.cmd`-tiedostoa. Pidä sovelluksen julkaisutiedostot samassa kansiossa.

Lähdekoodista rakentamiseen tarvitset Windowsin ja .NET 9 SDK:n. Ohjelman ajamiseen riittää .NET 9 Desktop Runtime. Kehitys ei vaadi Tool32:ta, EDIABASia tai autoa.

```powershell
./scripts/build.ps1
./Kaynnista.cmd
```

Tai suoraan SDK:lla:

```powershell
dotnet build BMWServiceTool.sln -c Release
dotnet run --project src/G30ServiceTool.App -c Release --no-build
```

## Toiminnot

- Huoltopäivä, kilometrit, tehdyt työt, ajankohtaisuus ja muistiinpanot.
- Paikallinen JSON-huoltokirja, joka säilyy ohjelman sulkemisen jälkeen.
- Päivä- ja kilometrivalidointi; myös vanhempien huoltojen lisääminen onnistuu, jos kilometrien järjestys säilyy.
- Aiemman tiedoston automaattinen varmuuskopio ennen muutosta ja atominen päähuoltokirjan korvaus.
- JSON-varmuuskopion vienti sekä lisäävä yhdistäminen. Tunnisteristiriita keskeyttää yhdistämisen.
- Oman koneen aktiiviset IPv4-sovittimet ja käsin annetun IP-osoitteen TCP-yhteystesti.
- Tool32-läppärin paikallisten tiedostojen tarkistus: EXE-rakenne, INI:n Interface/EcuPath ja valitun kansion PRG/GRP-tiedostot.
- Valitun merkinnän Tool32-tarkasteluluonnos `.txt`-tiedostona.

Sovellus käyttää yhtä paikallista huoltokirjaa tälle autolle. Automaattista VIN-tunnistusta tai usean auton erottelua ei vielä ole. Älä yhdistä toisen auton tietoja tähän huoltokirjaan.

## Jos Tool32 on toisella läppärillä

Siirrä `artifacts/G30ServiceTool-win-x64.zip` Tool32-läppärille, pura se kokonaan ja avaa siellä `G30ServiceTool.exe`. Vaihtoehtoisesti siirrä koko `artifacts/app`-kansio. `./scripts/build.ps1 -SelfContained` muodostaa Windows x64 -paketin, jossa on myös .NET-ajoympäristö. Ilman tätä valintaa läppärillä pitää olla .NET 9 Desktop Runtime. Paketissa on lyhyt `ALOITA-TASTA.txt`-ohje.

1. Avaa **Yhteys ja Tool32** -välilehti.
2. Valitse **Selaa…** ja kyseisen koneen `Tool32.exe`.
3. Anna ECU-kansio tarvittaessa, esimerkiksi jos INI:n `EcuPath` on suhteellinen tai tiedostot ovat muualla.
4. Paina **Tarkista paikalliset tiedostot**. Raportin voi kopioida keskusteluun.

Tämä tarkistus ei tarvitse autoa. Se ei käynnistä Tool32:ta, lataa DLL:ää tai muuta EDIABAS-asetuksia. `nbtevo.prg`-tiedoston löytyminen tarkoittaa vain, että tiedosto on valitussa kansiossa; auton yksikköä ja huoltojobien tukea ei ole vielä tunnistettu. Suhteellisia tai UNC-muotoisia ECU-polkuja ei seurata automaattisesti. Tarkistus käsittelee vain valitun Tool32:n vieressä olevan INI:n, ei järjestelmän kaikkia mahdollisia EDIABAS-asennuksia.

## Autolle kirjoittamisen tila

**Versio 0.1 ei vielä kirjoita iDriveen.** Tool32-luonnos perustuu NBT:llä testattuun avoimen lähdekoodin viitemuotoon. Lähdeprojektin tekijä ei vahvista NBT EVO -yhteensopivuutta. Aiemman keskustelun väite valmiiksi ratkaistusta G30/NBT EVO -kirjoitustavasta oli siten liian varma.

Luonnos sisältää `g_mmi`-ryhmän ehdotetun `steuern_servicehistory_add`-rivin, käyttäjän antaman korjaamotunnuksen ja itse tehdyn huollon lipun. Sovellus ei tuota ajettavaa `.tst`-tiedostoa eikä vaihda ryhmää oletuksena `nbtevo`:ksi. Älä aja viiteriviä autoon tämän luonnoksen perusteella. Luonnoksen korjaamotunnus, nollakentät, tilakoodit ja huolto-ID:t vaativat tarkistuksen oman auton job-kuvausta vasten. Muistiinpanot säilyvät vain paikallisessa JSON:ssa.

ENET-testi avaa TCP-yhteyden ja sulkee sen lähettämättä sovellustason dataa. Tavalliset portit ovat HSFZ `6801` ja DoIP `13400`. TCP-vastaus ei vahvista auton identiteettiä, head unit -tyyppiä tai kirjoitustukea. Ohjelma ei skannaa verkkoa, muuta verkkosovittimia eikä ota yhteyttä autoon automaattisesti.

Ohjelmassa ei ole auton historian tyhjennyskomentoa, BMW-verkkotietokannan päivitystä tai CBS-nollausta.

## Tiedostot ja varmuuskopiointi

```text
%LOCALAPPDATA%/G30ServiceTool/
  history.json
  .history.lock
  backups/history-<aika>-<tunniste>.json
```

Ensimmäinen merkintä luo huoltokirjan. Seuraavissa muutoksissa varmuuskopio sisältää päähuoltokirjan edellisen version. Kopiointivirhe keskeyttää muutoksen. Tiedostolukko estää kahta sovellusinstanssia hukkaamasta toistensa merkintöjä.

Varmuuskopiot ovat samalla levyllä. Vie ajoittain JSON-kopio myös muualle. Varmuuskopioissa on vain tässä ohjelmassa kirjatut tiedot; autosta ei vielä lueta historiaa. Tiedot tallennetaan selväkielisinä Windows-käyttäjäsi kansioon. Sovelluksessa ei ole telemetriaa tai pilvisynkronointia.

Jos päähuoltokirja on vioittunut, ohjelma estää uudet tallennukset. Säilytä vioittunut tiedosto erikseen, tarkista uusin ehjä `backups`-kopio ja palauta se `history.json`-nimellä ohjelman ollessa suljettuna. Päivitä tämän jälkeen näkymä tai käynnistä ohjelma uudelleen. Ohjelma ei tyhjennä vioittunutta tiedostoa automaattisesti.

## Testit

Testiajuri käyttää vain .NETin mukana tulevia kirjastoja, joten testipaketteja ei tarvitse ladata NuGetista.

```powershell
./scripts/test.ps1
```

Testit tarkistavat validoinnin, historian säilymisen, aiemman tiedoston varmuuskopion, virhetilanteet, tiedostolukon, lisäävän tuonnin, luonnoksen kentät sekä WPF-lomakkeen tallennuksen ja uudelleenavauksen. TCP-testissä paikallinen loopback-palvelin varmistaa, ettei sovellustason tavujakaan lähetetä. Testit eivät ota yhteyttä autoon. Testiajuri palauttaa virhekoodin epäonnistuessaan; pelkkä `dotnet test` ei suorita tätä ajuria.

Käyttöliittymän renderöidyt tarkastuskuvat tallennetaan `artifacts/qa`-kansioon. Testidata on synteettistä ja testien tilapäiskansio poistetaan ajon jälkeen.

## Projektin rakenne

```text
src/G30ServiceTool.Core/    Tietomalli, validointi, tallennus, TCP-testi, luonnos
src/G30ServiceTool.App/     WPF-käyttöliittymä
tests/G30ServiceTool.Tests/ Testiajuri ja WPF-renderöinti
docs/                      Protokollaselvitys ja jatkokehitys
scripts/                   Rakennus ja testaus
.github/workflows/         Windows-CI ja ladattava sovellusartefakti
```

Git-etärepo on `https://github.com/jimmb5/BMWServiceTool.git`. Huoltotiedot, luonnokset, varmuuskopiot ja rakennustulokset on rajattu versionhallinnan ulkopuolelle. GitHub Actions rakentaa ja testaa Windowsilla sekä tuottaa ladattavan `G30ServiceTool-win-x64`-artefaktin; julkista Release-julkaisua se ei tee.

Tekniset lähteet ja avoimet kysymykset: [docs/protocol-research.md](docs/protocol-research.md). Viiteprojektin lisenssi: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
