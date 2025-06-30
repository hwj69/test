<template>
  <div class="board">
    <HanoiTower
      v-for="(disks, idx) in towers"
      :key="idx"
      :disks="disks"
      :index="idx"
      :isSelected="selectedTowerIndex === idx"
      @tower-click="$emit('tower-click', idx)"
      :palette="palette"
    />
  </div>
</template>

<script setup>
const props = defineProps({
  towers: Array,
  selectedTowerIndex: Number,
  palette: Array,
});
const emits = defineEmits(['tower-click']);
import HanoiTower from './HanoiTower.vue';
</script>

<style scoped>
.board {
  display: flex;
  justify-content: center;
  gap: 2rem;
  margin-bottom: 1rem;
}
</style>
