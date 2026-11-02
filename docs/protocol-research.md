# Protokollaselvitys ja jatkokehitys

Tarkistettu 2.11.2026. Autoa ei ole kytketty kehityksen aikana. Käyttöliittymän automalliteksti ei ole autosta luettu tunniste.

## Varmistetut lähdehavainnot

1. [theDeniZ/ServiceManager](https://github.com/theDeniZ/ServiceManager): README rajaa testauksen NBT-yksikköön ja jättää CIC/NBT EVO -toimivuuden avoimeksi. Ohjelma muodostaa Tool32-testitiedostoja, eikä itse kirjoita autoon.
2. [service.cpp](https://github.com/theDeniZ/ServiceManager/blob/master/service.cpp): lähdekoodin sarjoitus käyttää järjestystä päivä;kuukausi;vuosi;0;kilometrit;BMW-lippu;korjaamotunnus;tila;työmäärä. Päivän ja kuukauden järjestys otettiin koodista, sillä README-esimerkkien päivät ovat keskenään epäjohdonmukaisia.
3. [serviceitem.h](https://github.com/theDeniZ/ServiceManager/blob/master/serviceitem.h) ja [serviceitem.cpp](https://github.com/theDeniZ/ServiceManager/blob/master/serviceitem.cpp): työ-ID ja tila sarjoitetaan neljän kentän ryhmänä. Numerot toteutuksessa ovat tämän viiteprojektin koodeja; niitä ei ole validoitu NBT EVO:lle.
4. [servicehistory.cpp](https://github.com/theDeniZ/ServiceManager/blob/master/servicehistory.cpp): lisäävän viiterivin ryhmä on `g_mmi` ja job `steuern_servicehistory_add`.
5. [EdiabasLibin tekijän verkkokonfiguraation dokumentaatio](https://github.com/uholeschak/ediabaslib/blob/master/docs/EdiabasLib.config_file.md): HSFZ-diagnostiikan tavallinen portti on 6801, DoIP:n 13400. Näitä käytetään vain TCP-testin porttiohjeena. EdiabasLib-koodia ei sisällytetä sovellukseen.

Näistä havainnoista ei seuraa, että `nbtevo.prg` hyväksyisi saman rivin tai että lisääminen säilyttäisi olemassa olevan historian kaikilla ohjelmistoversioilla. Siksi v0.1 vie vain tarkastelutekstiä eikä ajettavaa testiä. Korjaamotunnusta ei päätellä tai esitä virallisena BMW-tunnuksena. Itse tehty huolto merkitään viiteluonnoksessa BMW-lipulla 0.

## Ennen historian lukua

v0.1.1 lisää paikallisen asennustarkistuksen sillä koneella, jossa Tool32 sijaitsee. Tarkistus lukee valitun EXE:n PE-otsikon, saman kansion EDIABAS.INI:n Configuration-osion Interface/EcuPath-arvot ja raportoi valitun ECU-kansion tiedostojen löytymisen. Se ei lataa DLL-tiedostoja tai suorita jobeja. Tiedoston olemassaolo ei vahvista yhteensopivuutta. Seuraavaksi tarvitaan Tool32-läppärillä tuotettu raportti ja oman auton yksikkötiedot.

- Selvitä autosta keskusyksikön tyyppi ja ohjelmistotunniste. Mallivuosi yksin ei vahvista yksikköä.
- Käytä käyttäjän laillisesti hankkiman EDIABAS-asennuksen omia SGBD/PRG-kuvauksia. Älä sisällytä BMW:n tiedostoja repoon.
- Dokumentoi tunnistuksen ja historian lukemisen jobit, argumentit, tulokset ja virhetilanteet. Lukuadapteriin ei saa liittää kirjoitusjobia.
- Tallenna alkuperäiset vastaukset erilliseen autokohtaiseen varmuuskopioon ja vertaile iDriven näkymään. Paikallisen JSON-kirjan kopio ei korvaa tätä.

## Ennen ensimmäistä kirjoitusta

- Vahvista lisäävän jobin semantiikka, PRG-versio, kentät ja huoltokoodit kyseiselle yksikölle. Kirjaa yhteensopivuusnäyttö testeineen.
- Varmista auton identiteetti ja vastaavuus paikalliseen huoltokirjaan; pelkkä avautunut TCP-portti ei riitä.
- Selvitä käytössä olevan historian kapasiteetti, järjestys ja mahdolliset ylikirjoitusvaikutukset. Älä ota käyttöön poistavaa tai koko historiaa korvaavaa toimintoa.
- Kytke kirjoitus erilliseen käyttäjän käynnistämään työnkulkuun: valitun merkinnän esikatselu, auton historian varmuuskopio, vakaa syöttöjännite, yksi lisäys, takaisinluku ja vertailu.
- Määrittele epäselvän kuittauksen käsittely. Älä yritä kirjoitusta automaattisesti uudelleen, sillä se voi lisätä kaksoiskappaleen.
- Testaa ensin erillisellä testiyksiköllä tai varmennetussa testijärjestelyssä.

BMW:n keskitetty historia ja CBS ovat eri toimintoja ja pysyvät tämän vaiheen ulkopuolella.
