# Käyttöliittymän valinnat

Windows/WPF-työpöytäsovellus. UI/UX Pro Max -haun `desktop software utility` tuloksista sovellettiin Minimalism & Swiss Style -suuntaa: selkeä hierarkia, vaaleat pinnat, sininen päätoiminto ja luettavat kontrastit. Ensimmäinen autoteemainen haku painottui markkinointisivuun ja hylättiin. Markkinointisivun hero- tai animaatioehdotuksia ei sovellettu työkalun työnkulkuun.

- Windowsin paikallinen Segoe UI; fontteja tai kuvia ei ladata verkosta.
- Yksi päätoiminto: paikallisen huoltomerkinnän tallennus, näkyvissä myös pienessä ikkunassa.
- Natiivit WPF-kentät, näkyvät tunnisteet, Tab-navigointi ja Alt-pikanäppäimet.
- Virheelliset kentät säilyttävät käyttäjän syötteen. Päivä-, kilometri- ja huoltovalintavirheet näytetään lomakkeella.
- TCP-portin vastaus ja auton tunnistus ilmaistaan erikseen.
- Pääikkunan minimikoko 980 × 650 DIP; sisältöjä voi vierittää. Pitkä Tool32-viiterivi vierittyy vaakasuunnassa.
- Liike- tai parallax-efektejä ei käytetä. Vain vaalea teema tässä versiossa.

Värien ja mittojen yhteiset tyylit ovat `Theme.xaml`-tiedostossa. Testiajuri renderöi päänäkymän, minimikoon ja muut välilehdet visuaalista tarkastusta varten.
