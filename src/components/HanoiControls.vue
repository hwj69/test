<template>
  <div class="controls">
    <label>
      Disques
      <input type="number" v-model.number="localDisks" min="1" />
    </label>
    <label>
      Tours
      <input type="number" v-model.number="localTowers" min="3" />
    </label>
    <label>
      Th\u00e8me
      <select v-model="localTheme">
        <option value="theme-defaut">D\u00e9faut</option>
        <option value="theme-foret">For\u00eat</option>
      </select>
    </label>
    <button @click="$emit('start-game')" :disabled="isSolving">D\u00e9marrer</button>
    <button @click="$emit('solve-game')" :disabled="isSolving">R\u00e9soudre</button>
  </div>
</template>

<script setup>
import { ref, watch } from 'vue';

const props = defineProps({
  isSolving: Boolean,
});
const emits = defineEmits(['change-disks', 'change-towers', 'change-theme', 'start-game', 'solve-game']);

const localDisks = ref(3);
const localTowers = ref(3);
const localTheme = ref('theme-defaut');

watch(localDisks, v => emits('change-disks', v));
watch(localTowers, v => emits('change-towers', v));
watch(localTheme, v => emits('change-theme', v));
</script>

<style scoped>
.controls {
  display: flex;
  gap: 1rem;
  justify-content: center;
  margin-bottom: 1rem;
}
</style>
