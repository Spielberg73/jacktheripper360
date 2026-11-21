// Títulos de canciones de juegos conocidos, por número de serie de la ROM.
// Solo se incluyen tablas fiables. Cualquier pista se puede renombrar desde la app.
(function () {
  const DB = [
    {
      // Sonic the Hedgehog 2 (todas las revisiones). IDs de música del desensamblado público (s2disasm).
      serial: /^GM 00001051/,
      game: 'Sonic the Hedgehog 2',
      titles: {
        0x81: '2P Results', 0x82: 'Emerald Hill Zone', 0x83: 'Mystic Cave Zone (2P)', 0x84: 'Oil Ocean Zone',
        0x85: 'Metropolis Zone', 0x86: 'Hill Top Zone', 0x87: 'Aquatic Ruin Zone', 0x88: 'Casino Night Zone (2P)',
        0x89: 'Casino Night Zone', 0x8A: 'Death Egg Zone', 0x8B: 'Mystic Cave Zone', 0x8C: 'Emerald Hill Zone (2P)',
        0x8D: 'Sky Chase Zone', 0x8E: 'Chemical Plant Zone', 0x8F: 'Wing Fortress Zone', 0x90: 'Hidden Palace Zone',
        0x91: 'Options', 0x92: 'Special Stage', 0x93: 'Boss', 0x94: 'Final Boss', 0x95: 'Ending',
        0x96: 'Super Sonic', 0x97: 'Invincibility', 0x98: 'Extra Life', 0x99: 'Title Screen', 0x9A: 'Act Clear',
        0x9B: 'Game Over', 0x9C: 'Continue', 0x9D: 'Chaos Emerald', 0x9E: 'Credits', 0x9F: 'Drowning'
      }
    }
  ];

  function lookup(serial, value) {
    const g = DB.find(d => d.serial.test(serial || ''));
    return g ? g.titles[value] || null : null;
  }

  window.NDTitles = { lookup };
})();
