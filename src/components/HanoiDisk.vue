<template>
  <div class="disk" :style="style"></div>
</template>

<script setup>
import { computed } from 'vue';
const props = defineProps({
  size: Number,
  color: String,
});
const style = computed(() => ({
  background: props.color,
  width: `${props.size * 20}px`,
  height: '20px',
  margin: '2px 0',
}));
</script>

<style scoped>
.disk {
  border-radius: 4px;
}
</style>
