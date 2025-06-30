<template>
  <div :class="currentTheme">
    <h1>Jeu de la Tour de Hano\u00ef</h1>
    <HanoiControls
      :isSolving="isSolving"
      @change-disks="numberOfDisks = $event"
      @change-towers="numberOfTowers = $event"
      @change-theme="changeTheme"
      @start-game="startGame"
      @solve-game="solveAutomatically"
    />
    <HanoiBoard
      :towers="towers"
      :selectedTowerIndex="selectedTowerIndex"
      @tower-click="handleTowerClick"
      :palette="currentPalette"
    />
    <p>{{ gameMessage }}</p>
    <p>Nombre de mouvements : {{ moveCount }}</p>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue';
import HanoiControls from './components/HanoiControls.vue';
import HanoiBoard from './components/HanoiBoard.vue';

const numberOfDisks = ref(3);
const numberOfTowers = ref(3);
const moveCount = ref(0);
const selectedTowerIndex = ref(null);
const gameMessage = ref('');
const towers = ref([]);
const isGameInProgress = ref(false);
const isSolving = ref(false);

const THEME_PALETTES = {
  'theme-defaut': ['#e57373', '#f06292', '#ba68c8', '#9575cd'],
  'theme-foret': ['#2e7d32', '#388e3c', '#43a047', '#66bb6a'],
};

const currentTheme = ref('theme-defaut');
const currentPalette = computed(() => THEME_PALETTES[currentTheme.value]);

function changeTheme(theme) {
  currentTheme.value = theme;
}

function startGame() {
  towers.value = [];
  for (let i = 0; i < numberOfTowers.value; i++) {
    towers.value.push([]);
  }
  towers.value[0] = Array.from({ length: numberOfDisks.value }, (_, i) => numberOfDisks.value - i);
  moveCount.value = 0;
  gameMessage.value = '';
  selectedTowerIndex.value = null;
  isGameInProgress.value = true;
}

function handleTowerClick(index) {
  if (!isGameInProgress.value) return;
  if (selectedTowerIndex.value === null) {
    if (towers.value[index].length === 0) return;
    selectedTowerIndex.value = index;
  } else {
    if (index === selectedTowerIndex.value) {
      selectedTowerIndex.value = null;
      return;
    }
    const fromTower = towers.value[selectedTowerIndex.value];
    const toTower = towers.value[index];
    const disk = fromTower[fromTower.length - 1];
    const topDest = toTower[toTower.length - 1];
    if (!topDest || disk < topDest) {
      toTower.push(fromTower.pop());
      moveCount.value++;
      checkVictory();
    }
    selectedTowerIndex.value = null;
  }
}

function checkVictory() {
  if (towers.value[ numberOfTowers.value - 1 ].length === numberOfDisks.value) {
    gameMessage.value = 'Victoire !';
    isGameInProgress.value = false;
  }
}

async function solveAutomatically() {
  if (isSolving.value) return;
  isSolving.value = true;
  startGame();
  await solveRecursive(numberOfDisks.value, 0, numberOfTowers.value - 1, 1);
  isSolving.value = false;
}

async function solveRecursive(n, from, to, aux) {
  if (n === 0) return;
  await solveRecursive(n - 1, from, aux, to);
  towers.value[to].push(towers.value[from].pop());
  moveCount.value++;
  await new Promise(r => setTimeout(r, 300));
  await solveRecursive(n - 1, aux, to, from);
  checkVictory();
}
</script>

<style scoped>
.theme-defaut {
  --bg: #fff;
  --text: #000;
}
.theme-foret {
  --bg: #e8f5e9;
  --text: #1b5e20;
}

div {
  background: var(--bg);
  color: var(--text);
  min-height: 100vh;
  padding: 1rem;
  text-align: center;
}
</style>
