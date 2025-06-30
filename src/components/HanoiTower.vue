<template>
  <div class="tower" :class="{selected: isSelected}" @click="$emit('tower-click', index)">
    <HanoiDisk
      v-for="(disk, i) in disks"
      :key="i"
      :size="disk"
      :color="palette[(disk - 1) % palette.length]"
    />
  </div>
</template>

<script setup>
const props = defineProps({
  disks: Array,
  index: Number,
  isSelected: Boolean,
  palette: Array,
});
import HanoiDisk from './HanoiDisk.vue';
</script>

<style scoped>
.tower {
  width: 120px;
  height: 200px;
  border: 2px solid #333;
  display: flex;
  flex-direction: column-reverse;
  align-items: center;
  padding: 4px;
  cursor: pointer;
}
.selected {
  border-color: red;
}
</style>
